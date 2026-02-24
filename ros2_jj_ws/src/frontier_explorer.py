#!/usr/bin/env python3

"""
Frontier Exploration Node per ROS2
Esplora autonomamente l'ambiente cercando i frontier (bordi tra zona mappata e sconosciuta)
e mandando il robot verso di essi tramite /cmd_vel.

Requisiti:
- slam_toolbox in esecuzione e che pubblica /map
- /scan disponibile
- /cmd_vel collegato al robot
"""

import rclpy
from rclpy.node import Node
from nav_msgs.msg import OccupancyGrid
from geometry_msgs.msg import Twist
from sensor_msgs.msg import LaserScan
import numpy as np
import random
import math


class FrontierExplorer(Node):

    def __init__(self):
        super().__init__('frontier_explorer')

        # Publisher per i comandi di velocità
        self.cmd_vel_pub = self.create_publisher(Twist, '/cmd_vel', 10)

        # Subscriber alla mappa e al LiDAR
        self.map_sub = self.create_subscription(
            OccupancyGrid, '/map', self.map_callback, 10)
        self.scan_sub = self.create_subscription(
            LaserScan, '/scan', self.scan_callback, 10)

        # Stato interno
        self.map_data = None
        self.map_info = None
        self.scan_ranges = []
        self.current_goal = None
        self.state = 'EXPLORING'  # EXPLORING, ROTATING, AVOIDING

        # Parametri
        self.linear_speed = 0.25      # m/s velocità avanti
        self.angular_speed = 0.5      # rad/s velocità rotazione
        self.obstacle_threshold = 0.4 # m distanza minima ostacolo
        self.frontier_min_size = 5    # celle minime per considerare un frontier
        self.goal_reached_dist = 0.3  # m distanza per considerare goal raggiunto

        # Timer principale di controllo (10 Hz)
        self.timer = self.create_timer(0.1, self.control_loop)

        # Contatori per gestione stati
        self.rotation_count = 0
        self.rotation_target = 0
        self.avoiding_count = 0

        self.get_logger().info('Frontier Explorer avviato!')
        self.get_logger().info('In attesa della mappa da slam_toolbox...')

    def map_callback(self, msg):
        """Riceve la mappa aggiornata da slam_toolbox"""
        self.map_data = np.array(msg.data).reshape(msg.info.height, msg.info.width)
        self.map_info = msg.info

    def scan_callback(self, msg):
        """Riceve i dati del LiDAR"""
        self.scan_ranges = msg.ranges

    def get_frontiers(self):
        """
        Trova i frontier nella mappa.
        Un frontier è una cella libera (0) adiacente a una cella sconosciuta (-1).
        Restituisce lista di coordinate (x, y) in metri nel frame della mappa.
        """
        if self.map_data is None:
            return []

        frontiers = []
        height, width = self.map_data.shape

        for row in range(1, height - 1):
            for col in range(1, width - 1):
                # Cella libera
                if self.map_data[row, col] == 0:
                    # Controlla se ha vicini sconosciuti (-1)
                    neighbors = [
                        self.map_data[row-1, col],
                        self.map_data[row+1, col],
                        self.map_data[row, col-1],
                        self.map_data[row, col+1]
                    ]
                    if -1 in neighbors:
                        # Converti da cella a coordinate mondo
                        x = col * self.map_info.resolution + self.map_info.origin.position.x
                        y = row * self.map_info.resolution + self.map_info.origin.position.y
                        frontiers.append((x, y))

        return frontiers

    def cluster_frontiers(self, frontiers, cluster_radius=0.5):
        """
        Raggruppa i frontier vicini in cluster e restituisce il centroide di ogni cluster.
        Questo evita di avere migliaia di frontier singoli.
        """
        if not frontiers:
            return []

        clusters = []
        used = [False] * len(frontiers)

        for i, frontier in enumerate(frontiers):
            if used[i]:
                continue
            cluster = [frontier]
            used[i] = True

            for j, other in enumerate(frontiers):
                if used[j]:
                    continue
                dist = math.sqrt((frontier[0]-other[0])**2 + (frontier[1]-other[1])**2)
                if dist < cluster_radius:
                    cluster.append(other)
                    used[j] = True

            if len(cluster) >= self.frontier_min_size:
                # Centroide del cluster
                cx = sum(p[0] for p in cluster) / len(cluster)
                cy = sum(p[1] for p in cluster) / len(cluster)
                clusters.append((cx, cy, len(cluster)))

        return clusters

    def get_robot_position(self):
        """
        Stima la posizione del robot dalla mappa (centro della mappa libera più recente).
        In un sistema reale useresti le TF, qui usiamo una stima semplice.
        """
        if self.map_info is None:
            return 0.0, 0.0

        # Usa l'origine della mappa come riferimento
        # Il robot parte sempre da 0,0 nel frame odom
        return 0.0, 0.0

    def is_obstacle_ahead(self, angle_range=45):
        """
        Controlla se c'è un ostacolo davanti al robot.
        angle_min=-180° → ranges[0] è DIETRO il robot
        angle_max=+180° → il DAVANTI è al centro dell'array (indice n//2)
        """
        if not self.scan_ranges:
            return False

        n = len(self.scan_ranges)
        center = n // 2  # indice 180° = davanti al robot
        spread = int(n * angle_range / 360)

        indices = list(range(center - spread, center + spread))

        for i in indices:
            if i < 0 or i >= n:
                continue
            r = self.scan_ranges[i]
            if math.isfinite(r) and 0.1 < r < self.obstacle_threshold:
                return True
        return False

    def is_obstacle_left(self):
        """Controlla ostacoli a sinistra (angolo positivo = sinistra in ROS)"""
        if not self.scan_ranges:
            return False
        n = len(self.scan_ranges)
        center = n // 2
        spread = int(n * 45 / 360)
        for i in range(center, center + spread):
            if i >= n:
                continue
            r = self.scan_ranges[i]
            if math.isfinite(r) and 0.1 < r < self.obstacle_threshold:
                return True
        return False

    def is_obstacle_right(self):
        """Controlla ostacoli a destra"""
        if not self.scan_ranges:
            return False
        n = len(self.scan_ranges)
        center = n // 2
        spread = int(n * 45 / 360)
        for i in range(center - spread, center):
            if i < 0:
                continue
            r = self.scan_ranges[i]
            if math.isfinite(r) and 0.1 < r < self.obstacle_threshold:
                return True
        return False

    def is_obstacle_left(self):
        """Controlla ostacoli a sinistra"""
        if not self.scan_ranges:
            return False
        n = len(self.scan_ranges)
        start = n // 4
        end = n // 2
        for i in range(start, end):
            if 0.05 < self.scan_ranges[i] < self.obstacle_threshold:
                return True
        return False

    def is_obstacle_right(self):
        """Controlla ostacoli a destra"""
        if not self.scan_ranges:
            return False
        n = len(self.scan_ranges)
        start = n // 2
        end = 3 * n // 4
        for i in range(start, end):
            if 0.05 < self.scan_ranges[i] < self.obstacle_threshold:
                return True
        return False

    def control_loop(self):
        """Loop principale di controllo del robot"""
        twist = Twist()

        if self.state == 'AVOIDING':
            # Evita ostacolo ruotando
            twist.linear.x = 0.0
            if self.is_obstacle_left():
                twist.angular.z = -self.angular_speed  # gira destra
            else:
                twist.angular.z = self.angular_speed   # gira sinistra

            self.avoiding_count += 1
            if self.avoiding_count > 20:  # dopo 2 secondi torna a esplorare
                self.avoiding_count = 0
                self.state = 'EXPLORING'
                self.get_logger().info('Ostacolo evitato, riprendo esplorazione')

        elif self.state == 'ROTATING':
            # Rotazione per allinearsi verso il frontier
            twist.linear.x = 0.0
            twist.angular.z = self.angular_speed
            self.rotation_count += 1
            if self.rotation_count >= self.rotation_target:
                self.rotation_count = 0
                self.state = 'EXPLORING'

        elif self.state == 'EXPLORING':
            # Controlla ostacoli
            if self.is_obstacle_ahead():
                self.get_logger().info('Ostacolo rilevato! Evito...')
                self.state = 'AVOIDING'
                self.avoiding_count = 0
                twist.linear.x = 0.0
                twist.angular.z = 0.0
            else:
                # Cerca frontier
                frontiers = self.get_frontiers()
                clusters = self.cluster_frontiers(frontiers)

                if clusters:
                    # Scegli il cluster più grande (più informativo)
                    best = max(clusters, key=lambda c: c[2])
                    self.get_logger().info(
                        f'Frontier trovato: ({best[0]:.2f}, {best[1]:.2f}), '
                        f'dimensione: {best[2]} celle')

                    # Vai avanti verso il frontier
                    twist.linear.x = self.linear_speed
                    twist.angular.z = 0.0

                    # Rotazione casuale ogni tanto per esplorare meglio
                    if random.random() < 0.02:  # 2% di probabilità per ciclo
                        self.state = 'ROTATING'
                        self.rotation_target = random.randint(5, 20)
                        self.rotation_count = 0
                else:
                    # Nessun frontier trovato — mapping completato o robot bloccato
                    if self.map_data is not None:
                        self.get_logger().info(
                            'Nessun frontier trovato! Mapping probabilmente completato.')
                        # Continua a girare per verificare
                        twist.linear.x = 0.0
                        twist.angular.z = self.angular_speed * 0.5
                    else:
                        # Mappa non ancora ricevuta, vai avanti piano
                        self.get_logger().info('Mappa non ancora disponibile, esploro...')
                        twist.linear.x = self.linear_speed * 0.5
                        twist.angular.z = 0.0

        self.cmd_vel_pub.publish(twist)

    def stop_robot(self):
        """Ferma il robot"""
        twist = Twist()
        self.cmd_vel_pub.publish(twist)


def main(args=None):
    rclpy.init(args=args)
    node = FrontierExplorer()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        node.get_logger().info('Esplorazione interrotta!')
        node.stop_robot()
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
