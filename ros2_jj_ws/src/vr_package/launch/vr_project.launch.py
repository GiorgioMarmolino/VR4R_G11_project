import os

from launch import LaunchDescription
from launch.actions import IncludeLaunchDescription, TimerAction, LogInfo
from launch.launch_description_sources import PythonLaunchDescriptionSource
from ament_index_python.packages import get_package_share_directory
from launch_ros.actions import Node


def generate_launch_description():
    pkg_share = get_package_share_directory('vr_project')

    # Path to map
    map_file = os.path.join(pkg_share, 'maps', 'unity_sim_env_map.yaml')

    # 1) ROS-TCP Endpoint
    ros_tcp_endpoint_node = Node(
        package='ros_tcp_endpoint',
        executable='default_server_endpoint',
        name='ros_tcp_endpoint',
        output='screen',
        parameters=[
            {'ROS_IP': '0.0.0.0'},
            {'ROS_TCP_PORT': 10000},
        ]
    )

    # 2) RViz2
    rviz2_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'rviz2.launch.py')
        )
    )

    # 3) Localization (AMCL)
    localization_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'localization.launch.py')
        ),
        launch_arguments={
            'map': map_file,
            'use_sim_time': 'true',
            'initial_pose_x': '0.0',
            'initial_pose_y': '-1.0',
            'initial_pose_yaw': '-1.5707'
        }.items()
    )

    # 4) Navigation (Nav2)
    navigation_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'navigation.launch.py')
        ),
        launch_arguments={
            'use_sim_time': 'true',
            'map_subscribe_transient_local': 'true'
        }.items()
    )

    # 5) Multiple trajectory generator
    trajectory_cmd = Node(
        package='vr_project',
        executable='candidate_paths.py',
        name='path_candidates_node',
        output='screen'
    )

    # 6) PathFollower
    pathFollower_cmd = Node(
        package='vr_project',
        executable='pathFollower.py',
        name='path_follower_node',
        output='screen'
    )
     # Sequenza di avvio con delays
    load_nodes = TimerAction(
        period=1.0,
        actions=[
            ros_tcp_endpoint_node,
            rviz2_cmd,
            LogInfo(msg='[1/5] TCP Endpoint + RViz2 started...'),

            TimerAction(
                period=5.0,
                actions=[
                    localization_cmd,
                    LogInfo(msg='[2/5] Localization (AMCL) started...'),

                    TimerAction(
                        period=3.0,
                        actions=[
                            navigation_cmd,
                            LogInfo(msg='[3/5] Navigation (Nav2) started...'),

                            TimerAction(
                                period=5.0,
                                actions=[
                                    trajectory_cmd,
                                    LogInfo(msg='[4/5] Trajectory generator started...'),

                                    TimerAction(
                                        period=2.0,
                                        actions=[
                                            pathFollower_cmd,
                                            LogInfo(msg='[5/5] Path follower started. System ready!')
                                        ]
                                    )
                                ]
                            )
                        ]
                    )
                ]
            )
        ]
    )

    return LaunchDescription([load_nodes])