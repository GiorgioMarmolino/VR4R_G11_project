import os
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription
from launch.conditions import IfCondition
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration, PathJoinSubstitution, Command, TextSubstitution
from launch_ros.actions import Node
from ament_index_python.packages import get_package_share_directory

def generate_launch_description():

    pkg_vr_project = get_package_share_directory('vr_project')
    #============ LAUNCH ARGUMENTS ===========
    rviz_launch_arg = DeclareLaunchArgument(
        'rviz', 
        default_value='true', # or set to 'true'
        description='Open RViz'
    )
    rviz_config_arg = DeclareLaunchArgument(
        'rviz_config', 
        default_value='rviz_cnfg.rviz',
        description='RViz config file'
    )
    sim_time_arg = DeclareLaunchArgument(
        'use_sim_time', 
        default_value='True',
        description='Flag to enable use_sim_time'
    )
    
    # Launch rviz
    rviz_node = Node(
        package='rviz2',
        executable='rviz2',
        arguments=['-d', PathJoinSubstitution(
            [pkg_vr_project, 'rviz', LaunchConfiguration('rviz_config')])],
        condition=IfCondition(LaunchConfiguration('rviz')),
        parameters=[
            {'use_sim_time': LaunchConfiguration('use_sim_time')},
        ]
    )

    
    launchDescriptionObject = LaunchDescription()
    launchDescriptionObject.add_action(rviz_launch_arg)
    launchDescriptionObject.add_action(rviz_config_arg)
    launchDescriptionObject.add_action(sim_time_arg)
    launchDescriptionObject.add_action(rviz_node)
    return launchDescriptionObject