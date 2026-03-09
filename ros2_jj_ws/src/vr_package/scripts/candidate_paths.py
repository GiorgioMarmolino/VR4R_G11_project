#!/usr/bin/env python3

"""
candidate_paths.py

Nodo ROS2 per la Shared Autonomy.
Ascolta /goal_pose, genera 3 traiettorie candidate chiamando
Nav2 /compute_path_to_pose con goal leggermente diversi,
e pubblica le traiettorie su:
  /candidate_path_0  → traiettoria ottimale diretta
  /candidate_path_1  → goal spostato a sinistra (+15° + offset)
  /candidate_path_2  → goal spostato a destra  (-15° + offset)
"""

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.callback_groups import ReentrantCallbackGroup, MutuallyExclusiveCallbackGroup
from rclpy.executors import MultiThreadedExecutor

from geometry_msgs.msg import PoseStamped, Quaternion
from nav_msgs.msg import Path
from nav2_msgs.action import ComputePathToPose

import math
import copy


def euler_to_quaternion(yaw: float) -> Quaternion:
    q = Quaternion()
    q.x = 0.0
    q.y = 0.0
    q.z = math.sin(yaw / 2.0)
    q.w = math.cos(yaw / 2.0)
    return q


def quaternion_to_yaw(q) -> float:
    siny_cosp = 2.0 * (q.w * q.z + q.x * q.y)
    cosy_cosp = 1.0 - 2.0 * (q.y * q.y + q.z * q.z)
    return math.atan2(siny_cosp, cosy_cosp)


def rotate_goal(goal: PoseStamped, angle_offset_deg: float,
                dist_offset: float = 0.0) -> PoseStamped:
    new_goal = copy.deepcopy(goal)
    yaw      = quaternion_to_yaw(goal.pose.orientation)
    new_yaw  = yaw + math.radians(angle_offset_deg)
    new_goal.pose.orientation = euler_to_quaternion(new_yaw)

    if dist_offset != 0.0:
        perp_angle = yaw + math.pi / 2.0
        new_goal.pose.position.x += dist_offset * math.cos(perp_angle)
        new_goal.pose.position.y += dist_offset * math.sin(perp_angle)

    return new_goal


class PathCandidatesNode(Node):

    VARIANTS = [
        {"name": "optimal", "angle_deg":   0.0, "dist_offset":  0.0},
        {"name": "left",    "angle_deg": +15.0, "dist_offset": +0.3},
        {"name": "right",   "angle_deg": -15.0, "dist_offset": -0.3},
    ]

    def __init__(self):
        super().__init__('path_candidates_node')
        self.get_logger().info('PathCandidatesNode avviato.')

        # Callback group separati per subscriber e action client
        self.sub_cb_group    = MutuallyExclusiveCallbackGroup()
        self.action_cb_group = ReentrantCallbackGroup()

        # Subscriber al goal
        self.goal_sub = self.create_subscription(
            PoseStamped,
            '/goal_pose',
            self.on_goal_received,
            10,
            callback_group=self.sub_cb_group
        )

        # Publisher per le 3 traiettorie
        self.path_pubs = []
        for i in range(len(self.VARIANTS)):
            pub = self.create_publisher(Path, f'/candidate_path_{i}', 10)
            self.path_pubs.append(pub)
            self.get_logger().info(f'Publisher creato: /candidate_path_{i}')

        # Action client Nav2
        self.nav2_client = ActionClient(
            self,
            ComputePathToPose,
            'compute_path_to_pose',
            callback_group=self.action_cb_group
        )

        self.computing = False

    def on_goal_received(self, msg: PoseStamped):
        self.get_logger().info(
            f'Goal ricevuto: x={msg.pose.position.x:.2f}, y={msg.pose.position.y:.2f}'
        )
        if self.computing:
            self.get_logger().warn('Già calcolando, skip.')
            return

        self.computing = True
        self.compute_all_paths(msg)
        self.computing = False

    def compute_all_paths(self, goal: PoseStamped):
        if not self.nav2_client.wait_for_server(timeout_sec=5.0):
            self.get_logger().error('Nav2 non disponibile!')
            return

        for i, variant in enumerate(self.VARIANTS):
            modified_goal = rotate_goal(goal, variant["angle_deg"], variant["dist_offset"])
            path = self.call_compute_path(modified_goal, variant["name"])

            if path is not None and len(path.poses) > 0:
                self.path_pubs[i].publish(path)
                self.get_logger().info(
                    f'Path {i} ({variant["name"]}): {len(path.poses)} pose → /candidate_path_{i}'
                )
            else:
                self.get_logger().warn(f'Path {i} ({variant["name"]}): fallito o vuoto.')
                empty = Path()
                empty.header.frame_id = 'map'
                empty.header.stamp    = self.get_clock().now().to_msg()
                self.path_pubs[i].publish(empty)

        self.get_logger().info('Calcolo completato.')

    def call_compute_path(self, goal_pose: PoseStamped, name: str):
        goal_msg             = ComputePathToPose.Goal()
        goal_msg.goal        = goal_pose
        goal_msg.planner_id  = ''
        goal_msg.use_start   = False

        # Invia goal e attendi con future — compatibile con MultiThreadedExecutor
        send_future = self.nav2_client.send_goal_async(goal_msg)

        # Busy wait compatibile con MultiThreadedExecutor
        timeout = 10.0
        elapsed = 0.0
        while not send_future.done() and elapsed < timeout:
            rclpy.spin_once(self, timeout_sec=0.1)
            elapsed += 0.1

        if not send_future.done():
            self.get_logger().error(f'Timeout send_goal {name}')
            return None

        goal_handle = send_future.result()
        if not goal_handle.accepted:
            self.get_logger().error(f'Goal {name} rifiutato')
            return None

        result_future = goal_handle.get_result_async()
        elapsed = 0.0
        while not result_future.done() and elapsed < timeout:
            rclpy.spin_once(self, timeout_sec=0.1)
            elapsed += 0.1

        if not result_future.done():
            self.get_logger().error(f'Timeout result {name}')
            return None

        return result_future.result().result.path


def main(args=None):
    rclpy.init(args=args)
    node = PathCandidatesNode()

    # MultiThreadedExecutor necessario per ReentrantCallbackGroup
    executor = MultiThreadedExecutor(num_threads=4)
    executor.add_node(node)

    try:
        executor.spin()
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()