import os

from launch import LaunchDescription
from launch.actions import IncludeLaunchDescription, TimerAction, LogInfo, ExecuteProcess
from launch.launch_description_sources import PythonLaunchDescriptionSource
from ament_index_python.packages import get_package_share_directory

def generate_launch_description():
    pkg_name = 'assignment2'
    pkg_share = get_package_share_directory(pkg_name)
    
    # Path to map
    map_file = os.path.join(pkg_share, 'maps', 'map_of_world.yaml')
    
    # Domain PDDL file
    domain_file = os.path.join(pkg_share, 'pddl', 'domain.pddl')

    # Simulation Launch
    spawn_robot_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'spawn_robot_aruco.launch.py')
        )
    )

    # Localization Launch (AMCL)
    localization_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'localization.launch.py')
        ),
        launch_arguments={
            'map': map_file,
            'use_sim_time': 'true',
            'initial_pose_x': '0.0',      # Matches spawn location
            'initial_pose_y': '-1.0',     # Matches spawn location
            'initial_pose_yaw': '-1.5707'
        }.items()
    )

    # Navigation Launch (Nav2)
    navigation_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'navigation.launch.py')
        ),
        launch_arguments={
            'use_sim_time': 'true',
            'map_subscribe_transient_local': 'true'
        }.items()
    )

    # PlanSys2 Launch 
    plansys2_cmd = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(
            os.path.join(pkg_share, 'launch', 'distributed_actions.launch.py')
        ),
        launch_arguments={
            'model_file': domain_file,
            'namespace': ''
        }.items()
    )

    # Mission Controller Execution in new terminal
    execute_plan_cmd = ExecuteProcess(
        cmd=[
            'xterm', '-e', 
            # 'gnome-terminal', '--',
            'ros2', 'run', 'assignment2', 'get_plan_and_execute'
        ],
        output='screen'
    )
    
    # Sequentially load nodes with delays
    load_nodes = TimerAction(
        period=1.0, 
        actions=[
            spawn_robot_cmd,
            LogInfo(msg='[1/5] Robot Spawning... Waiting 5s'),
            
            TimerAction(
                period=5.0,
                actions=[
                    localization_cmd,
                    LogInfo(msg='[2/5] Localization (AMCL) Started... Waiting 3s'),
                    
                    TimerAction(
                        period=3.0,
                        actions=[
                            navigation_cmd,
                            LogInfo(msg='[3/5] Navigation (Nav2) Started... Waiting 4s'),
                            
                            TimerAction(
                                period=4.0,
                                actions=[
                                    plansys2_cmd,
                                    LogInfo(msg='[4/5] PlanSys2 & Actions Started... Waiting 5s for system ready'),
                                    
                                    TimerAction(
                                        period=5.0,
                                        actions=[
                                            LogInfo(msg='[5/5] Launching Mission Controller in new terminal...'),
                                            execute_plan_cmd
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