#!/usr/bin/env python3

"""
path_follower_node.py

Ascolta /edited_path pubblicato da Unity e lo esegue
usando l'action FollowPath di Nav2, bypassando il planner.
Ascolta anche /cancel_navigation (std_msgs/Bool) per fermare il robot.
"""

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.executors import MultiThreadedExecutor
from rclpy.callback_groups import ReentrantCallbackGroup

from nav_msgs.msg import Path
from nav2_msgs.action import FollowPath, NavigateToPose
from std_msgs.msg import Bool
from geometry_msgs.msg import Twist

import threading
import asyncio


class PathFollowerNode(Node):

    def __init__(self):
        super().__init__('path_follower_node')

        cb = ReentrantCallbackGroup()

        # Subscriber al path editato da Unity
        self.path_sub = self.create_subscription(
            Path, '/edited_path', self.on_path_received, 10,
            callback_group=cb)

        # Subscriber al cancel da Unity
        self.cancel_sub = self.create_subscription(
            Bool, '/cancel_navigation', self.on_cancel_received, 10,
            callback_group=cb)

        # Publisher velocità zero — ferma subito il robot
        self.cmd_vel_pub = self.create_publisher(Twist, '/cmd_vel', 10)

        # Action client FollowPath — esegue il path senza ricalcolo
        self.follow_client = ActionClient(
            self, FollowPath, '/follow_path',
            callback_group=cb)

        # Event loop asyncio in thread separato
        self.loop = asyncio.new_event_loop()
        threading.Thread(target=self.loop.run_forever, daemon=True).start()

        # Goal handle corrente — salvato per poterlo cancellare
        self.current_goal_handle = None

        self.get_logger().info('PathFollowerNode avviato.')
        self.get_logger().info('  /edited_path       → esegue il path')
        self.get_logger().info('  /cancel_navigation → ferma il robot')

    # ── Path ricevuto ────────────────────────────────────────────────────────

    def on_path_received(self, msg: Path):
        if len(msg.poses) == 0:
            self.get_logger().warn('Path vuoto ricevuto, ignoro.')
            return
        self.get_logger().info(
            f'Path editato ricevuto: {len(msg.poses)} pose. Esecuzione...')
        asyncio.run_coroutine_threadsafe(
            self.execute_path_async(msg), self.loop)

    async def execute_path_async(self, path: Path):
        loop = asyncio.get_event_loop()

        if not self.follow_client.wait_for_server(timeout_sec=5.0):
            self.get_logger().error('FollowPath action server non disponibile!')
            return

        goal = FollowPath.Goal()
        goal.path                = path
        goal.controller_id       = ''
        goal.goal_checker_id     = ''
        goal.progress_checker_id = ''

        self.get_logger().info('Invio path a FollowPath...')

        send_future = self.follow_client.send_goal_async(goal)
        goal_handle = await asyncio.wait_for(
            loop.run_in_executor(None, lambda: self._wait(send_future)),
            timeout=10.0)

        if goal_handle is None or not goal_handle.accepted:
            self.get_logger().error('FollowPath goal rifiutato!')
            return

        # Salva il goal handle — on_cancel_received lo userà per cancellare
        self.current_goal_handle = goal_handle
        self.get_logger().info('FollowPath accettato. Robot in esecuzione...')

        result_future = goal_handle.get_result_async()
        result = await asyncio.wait_for(
            loop.run_in_executor(None, lambda: self._wait(result_future)),
            timeout=120.0)

        self.current_goal_handle = None

        if result is not None:
            self.get_logger().info('FollowPath completato! Goal raggiunto.')
        else:
            self.get_logger().warn('FollowPath timeout o fallito.')

    # ── Cancel ricevuto ──────────────────────────────────────────────────────

    def on_cancel_received(self, msg: Bool):
        if not msg.data:
            return
        self.get_logger().info('Cancel ricevuto — fermo il robot.')
        asyncio.run_coroutine_threadsafe(self.cancel_async(), self.loop)

    async def cancel_async(self):
        # 1. Cancella il goal corrente se esiste
        if self.current_goal_handle is not None:
            try:
                cancel_future = self.current_goal_handle.cancel_goal_async()
                loop = asyncio.get_event_loop()
                await asyncio.wait_for(
                    loop.run_in_executor(None, lambda: self._wait(cancel_future)),
                    timeout=3.0)
                self.get_logger().info('Goal FollowPath cancellato.')
            except Exception as e:
                self.get_logger().warn(f'Errore cancel goal: {e}')
            self.current_goal_handle = None

        # 2. Velocità zero per sicurezza
        stop = Twist()
        for _ in range(10):
            self.cmd_vel_pub.publish(stop)
            await asyncio.sleep(0.05)

        self.get_logger().info('Robot fermato.')

    # ── Utility ─────────────────────────────────────────────────────────────

    def _wait(self, future, timeout=30.0):
        import time
        elapsed = 0.0
        while not future.done() and elapsed < timeout:
            time.sleep(0.05)
            elapsed += 0.05
        return future.result() if future.done() else None


def main(args=None):
    rclpy.init(args=args)
    node = PathFollowerNode()

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