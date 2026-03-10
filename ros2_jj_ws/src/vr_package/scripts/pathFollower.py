#!/usr/bin/env python3

"""
path_follower_node.py

Ascolta /edited_path pubblicato da Unity e lo esegue
usando l'action FollowPath di Nav2, bypassando il planner.
Il robot seguirà esattamente la traiettoria modificata dall'utente.
"""

import rclpy
from rclpy.node import Node
from rclpy.action import ActionClient
from rclpy.executors import MultiThreadedExecutor
from rclpy.callback_groups import ReentrantCallbackGroup

from nav_msgs.msg import Path
from nav2_msgs.action import FollowPath, NavigateToPose
from geometry_msgs.msg import PoseStamped

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

        # Action client FollowPath — esegue il path senza ricalcolo
        self.follow_client = ActionClient(
            self, FollowPath, '/follow_path',
            callback_group=cb)

        # Action client NavigateToPose — per cancellare navigazione corrente
        self.nav_client = ActionClient(
            self, NavigateToPose, '/navigate_to_pose',
            callback_group=cb)

        # Event loop asyncio in thread separato
        self.loop = asyncio.new_event_loop()
        threading.Thread(target=self.loop.run_forever, daemon=True).start()

        self.get_logger().info('PathFollowerNode avviato. In ascolto su /edited_path.')

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

        # Aspetta FollowPath server
        if not self.follow_client.wait_for_server(timeout_sec=5.0):
            self.get_logger().error('FollowPath action server non disponibile!')
            return

        # Costruisci il goal
        goal = FollowPath.Goal()
        goal.path            = path
        goal.controller_id   = ''   # usa controller default
        goal.goal_checker_id = ''
        goal.progress_checker_id = ''

        self.get_logger().info('Invio path a FollowPath...')

        # Invia e aspetta accettazione
        send_future = self.follow_client.send_goal_async(goal)
        goal_handle = await asyncio.wait_for(
            loop.run_in_executor(None, lambda: self._wait(send_future)),
            timeout=10.0)

        if goal_handle is None or not goal_handle.accepted:
            self.get_logger().error('FollowPath goal rifiutato!')
            return

        self.get_logger().info('FollowPath accettato. Robot in esecuzione...')

        # Aspetta completamento
        result_future = goal_handle.get_result_async()
        result = await asyncio.wait_for(
            loop.run_in_executor(None, lambda: self._wait(result_future)),
            timeout=120.0)

        if result is not None:
            self.get_logger().info('FollowPath completato! Goal raggiunto.')
        else:
            self.get_logger().warn('FollowPath timeout o fallito.')

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