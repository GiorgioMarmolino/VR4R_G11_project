#!/usr/bin/env python3

"""
candidate_paths.py

Approccio: due nodi ROS2 separati nello stesso processo.
- GoalListenerNode: ascolta /goal_pose (non viene mai bloccato)
- PathComputerNode: calcola i path con il service (gira in thread separato)
"""

import rclpy
from rclpy.node import Node
from rclpy.executors import SingleThreadedExecutor, MultiThreadedExecutor
from rclpy.callback_groups import ReentrantCallbackGroup

from geometry_msgs.msg import PoseStamped, Quaternion
from nav_msgs.msg import Path
from nav2_msgs.action import ComputePathToPose
from rclpy.action import ActionClient

import math
import copy
import threading
import queue


def euler_to_quaternion(yaw: float) -> Quaternion:
    q = Quaternion()
    q.z = math.sin(yaw / 2.0)
    q.w = math.cos(yaw / 2.0)
    return q


def quaternion_to_yaw(q) -> float:
    siny_cosp = 2.0 * (q.w * q.z + q.x * q.y)
    cosy_cosp = 1.0 - 2.0 * (q.y * q.y + q.z * q.z)
    return math.atan2(siny_cosp, cosy_cosp)


def rotate_goal(goal: PoseStamped, angle_deg: float,
                dist_offset: float = 0.0) -> PoseStamped:
    new_goal = copy.deepcopy(goal)
    yaw      = quaternion_to_yaw(goal.pose.orientation)
    new_yaw  = yaw + math.radians(angle_deg)
    new_goal.pose.orientation = euler_to_quaternion(new_yaw)
    if dist_offset != 0.0:
        perp = yaw + math.pi / 2.0
        new_goal.pose.position.x += dist_offset * math.cos(perp)
        new_goal.pose.position.y += dist_offset * math.sin(perp)
    return new_goal


VARIANTS = [
    {"name": "optimal", "angle_deg":   0.0, "dist_offset":  0.0},
    {"name": "left",    "angle_deg": +15.0, "dist_offset": +0.3},
    {"name": "right",   "angle_deg": -15.0, "dist_offset": -0.3},
]


class GoalListenerNode(Node):
    """Ascolta /goal_pose e mette i goal in una coda."""

    def __init__(self, goal_queue: queue.Queue):
        super().__init__('goal_listener_node')
        self.goal_queue = goal_queue

        self.create_subscription(
            PoseStamped, '/goal_pose', self.on_goal, 10)

        self.get_logger().info('GoalListenerNode: in ascolto su /goal_pose')

    def on_goal(self, msg: PoseStamped):
        self.get_logger().info(
            f'Goal ricevuto: x={msg.pose.position.x:.2f}, y={msg.pose.position.y:.2f}')
        # Svuota la coda (un solo goal alla volta)
        while not self.goal_queue.empty():
            try: self.goal_queue.get_nowait()
            except: pass
        self.goal_queue.put(msg)


class PathComputerNode(Node):
    """Calcola i path e pubblica le traiettorie candidate."""

    def __init__(self):
        super().__init__('path_candidates_node')

        self.path_pubs = []
        for i in range(3):
            pub = self.create_publisher(Path, f'/candidate_path_{i}', 10)
            self.path_pubs.append(pub)
            self.get_logger().info(f'Publisher creato: /candidate_path_{i}')

        self.path_client = ActionClient(
            self, ComputePathToPose, '/compute_path_to_pose')

    def compute_all(self, goal: PoseStamped):
        if not self.path_client.wait_for_server(timeout_sec=5.0):
            self.get_logger().error('Action /compute_path_to_pose non disponibile!')
            return

        for i, variant in enumerate(VARIANTS):
            modified = rotate_goal(goal, variant["angle_deg"], variant["dist_offset"])
            path = self.call_service_sync(modified, variant["name"])

            if path and len(path.poses) > 0:
                self.path_pubs[i].publish(path)
                self.get_logger().info(
                    f'Path {i} ({variant["name"]}): {len(path.poses)} pose → /candidate_path_{i}')
            else:
                self.get_logger().warn(f'Path {i} ({variant["name"]}): fallito.')
                empty = Path()
                empty.header.frame_id = 'map'
                empty.header.stamp = self.get_clock().now().to_msg()
                self.path_pubs[i].publish(empty)

        self.get_logger().info('Calcolo completato.')

    def call_service_sync(self, goal_pose: PoseStamped, name: str):
        """Chiamata sincrona all'action client."""
        goal_msg            = ComputePathToPose.Goal()
        goal_msg.goal       = goal_pose
        goal_msg.planner_id = ''
        goal_msg.use_start  = False

        # Invia goal e aspetta accettazione
        future = self.path_client.send_goal_async(goal_msg)
        rclpy.spin_until_future_complete(self, future, timeout_sec=10.0)

        if not future.done():
            self.get_logger().error(f'Timeout invio goal {name}')
            return None

        goal_handle = future.result()
        if not goal_handle.accepted:
            self.get_logger().error(f'Goal {name} rifiutato')
            return None

        # Aspetta risultato
        result_future = goal_handle.get_result_async()
        rclpy.spin_until_future_complete(self, result_future, timeout_sec=10.0)

        if not result_future.done():
            self.get_logger().error(f'Timeout risultato {name}')
            return None

        return result_future.result().result.path


def main(args=None):
    rclpy.init(args=args)

    goal_queue = queue.Queue()

    # Nodo listener — gira nel thread principale
    listener = GoalListenerNode(goal_queue)

    # Nodo computer — gira in un thread separato con suo executor
    computer = PathComputerNode()
    computer_executor = SingleThreadedExecutor()
    computer_executor.add_node(computer)

    computer_thread = threading.Thread(
        target=computer_executor.spin, daemon=True)
    computer_thread.start()

    # Executor principale per il listener
    listener_executor = SingleThreadedExecutor()
    listener_executor.add_node(listener)

    listener.get_logger().info('Sistema avviato. In attesa di goal...')

    try:
        while rclpy.ok():
            # Spin listener per ricevere goal
            listener_executor.spin_once(timeout_sec=0.1)

            # Controlla se c'è un nuovo goal da processare
            try:
                goal = goal_queue.get_nowait()
                computer.get_logger().info('Calcolo traiettorie...')
                computer.compute_all(goal)
            except queue.Empty:
                pass

    except KeyboardInterrupt:
        pass
    finally:
        listener_executor.shutdown()
        computer_executor.shutdown()
        listener.destroy_node()
        computer.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()