#!/usr/bin/env python3

"""
candidate_paths.py

Nodo ROS2 per la Shared Autonomy.
Usa async/await per evitare conflitti con l'executor.
"""

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.callback_groups import ReentrantCallbackGroup
from rclpy.executors import MultiThreadedExecutor

from geometry_msgs.msg import PoseStamped, Quaternion
from nav_msgs.msg import Path
from nav2_msgs.action import ComputePathToPose

import math
import copy
import asyncio
import threading


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


class PathCandidatesNode(Node):

    VARIANTS = [
        {"name": "optimal", "angle_deg":   0.0, "dist_offset":  0.0},
        {"name": "left",    "angle_deg": +15.0, "dist_offset": +0.3},
        {"name": "right",   "angle_deg": -15.0, "dist_offset": -0.3},
    ]

    def __init__(self):
        super().__init__('path_candidates_node')

        cb = ReentrantCallbackGroup()

        self.goal_sub = self.create_subscription(
            PoseStamped, '/goal_pose', self.on_goal_received, 10,
            callback_group=cb)

        self.path_pubs = []
        for i in range(3):
            pub = self.create_publisher(Path, f'/candidate_path_{i}', 10)
            self.path_pubs.append(pub)
            self.get_logger().info(f'Publisher creato: /candidate_path_{i}')

        self.nav2_client = ActionClient(
            self, ComputePathToPose, 'compute_path_to_pose',
            callback_group=cb)

        # Event loop asyncio in thread separato
        self.loop = asyncio.new_event_loop()
        self.loop_thread = threading.Thread(
            target=self.loop.run_forever, daemon=True)
        self.loop_thread.start()

        self.get_logger().info('PathCandidatesNode avviato.')

    def on_goal_received(self, msg: PoseStamped):
        self.get_logger().info(
            f'Goal ricevuto: x={msg.pose.position.x:.2f}, y={msg.pose.position.y:.2f}')
        # Lancia il calcolo nell'event loop asyncio separato
        asyncio.run_coroutine_threadsafe(
            self.compute_all_paths_async(msg), self.loop)

    async def compute_all_paths_async(self, goal: PoseStamped):
        if not self.nav2_client.wait_for_server(timeout_sec=5.0):
            self.get_logger().error('Nav2 non disponibile!')
            return

        for i, variant in enumerate(self.VARIANTS):
            modified = rotate_goal(goal, variant["angle_deg"], variant["dist_offset"])
            path = await self.compute_path_async(modified, variant["name"])

            if path and len(path.poses) > 0:
                self.path_pubs[i].publish(path)
                self.get_logger().info(
                    f'Path {i} ({variant["name"]}): {len(path.poses)} pose → /candidate_path_{i}')
            else:
                self.get_logger().warn(f'Path {i} ({variant["name"]}): fallito.')
                empty = Path()
                empty.header.frame_id = 'map'
                empty.header.stamp    = self.get_clock().now().to_msg()
                self.path_pubs[i].publish(empty)

        self.get_logger().info('Calcolo completato.')

    async def compute_path_async(self, goal_pose: PoseStamped, name: str):
        """Chiama Nav2 in modo asincrono senza bloccare il subscriber."""
        goal_msg            = ComputePathToPose.Goal()
        goal_msg.goal       = goal_pose
        goal_msg.planner_id = ''
        goal_msg.use_start  = False

        # Converti il future ROS in awaitable asyncio
        loop = asyncio.get_event_loop()

        send_future = self.nav2_client.send_goal_async(goal_msg)
        goal_handle = await asyncio.wait_for(
            loop.run_in_executor(None, send_future.result), timeout=10.0)

        if goal_handle is None or not goal_handle.accepted:
            self.get_logger().error(f'Goal {name} rifiutato')
            return None

        result_future = goal_handle.get_result_async()
        result = await asyncio.wait_for(
            loop.run_in_executor(None, result_future.result), timeout=10.0)

        if result is None:
            return None

        return result.result.path


def main(args=None):
    rclpy.init(args=args)
    node = PathCandidatesNode()

    executor = MultiThreadedExecutor(num_threads=4)
    executor.add_node(node)

    try:
        executor.spin()
    except KeyboardInterrupt:
        pass
    finally:
        node.loop.call_soon_threadsafe(node.loop.stop)
        node.loop_thread.join()
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()