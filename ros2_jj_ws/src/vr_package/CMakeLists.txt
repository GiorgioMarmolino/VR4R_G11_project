cmake_minimum_required(VERSION 3.8)
project(vr_project)

if(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# find dependencies
find_package(ament_cmake REQUIRED)
find_package(rclcpp REQUIRED)
find_package(plansys2_pddl_parser REQUIRED)
find_package(plansys2_msgs REQUIRED)
find_package(plansys2_domain_expert REQUIRED)
find_package(plansys2_planner REQUIRED)
find_package(plansys2_problem_expert REQUIRED)
find_package(plansys2_executor REQUIRED)
find_package(rclcpp_action REQUIRED)
find_package(geometry_msgs REQUIRED)
find_package(nav2_msgs REQUIRED)
find_package(nav_msgs REQUIRED)
find_package(sensor_msgs REQUIRED)
find_package(cv_bridge REQUIRED)
find_package(OpenCV REQUIRED)
find_package(tf2 REQUIRED)
find_package(tf2_geometry_msgs REQUIRED)
find_package(image_transport REQUIRED)
find_package(rosidl_default_generators REQUIRED)
find_package(yaml-cpp REQUIRED)
find_package(ament_index_cpp REQUIRED)

# Add message generation
rosidl_generate_interfaces(${PROJECT_NAME}
  "msg/MarkerDetection.msg"
)

include_directories(
  ${cv_bridge_INCLUDE_DIRS}
  ${OpenCV_INCLUDE_DIRS}
  ${sensor_msgs_INCLUDE_DIRS}
)

include_directories(
  ${OpenCV_INCLUDE_DIRS}
)

add_executable(get_plan src/getplan.cpp)
add_executable(get_plan_and_execute src/getplan_and_execute.cpp)
add_executable(move_action_node src/actions/move_action_node.cpp)
add_executable(take_picture_action_node src/actions/take_picture_action_node.cpp)
add_executable(detect_marker_action_node src/actions/detect_marker_action_node.cpp)
add_executable(process_last_image_action_node src/actions/process_last_image_action_node.cpp)

ament_target_dependencies(get_plan
  rclcpp
  plansys2_pddl_parser
  plansys2_msgs
  plansys2_domain_expert
  plansys2_planner
  plansys2_problem_expert
)

ament_target_dependencies(get_plan_and_execute
  rclcpp
  plansys2_pddl_parser
  plansys2_msgs
  plansys2_domain_expert
  plansys2_planner
  plansys2_problem_expert
  plansys2_executor
  rclcpp_action
)

ament_target_dependencies(move_action_node
  rclcpp
  rclcpp_action
  plansys2_executor
  plansys2_msgs
  geometry_msgs
  nav2_msgs
  yaml-cpp
  ament_index_cpp
)

ament_target_dependencies(take_picture_action_node
  rclcpp
  rclcpp_action
  plansys2_executor
  plansys2_msgs
  geometry_msgs
  nav2_msgs
  sensor_msgs      
  cv_bridge        
  OpenCV           
  image_transport  
  sensor_msgs
  cv_bridge
)

ament_target_dependencies(detect_marker_action_node
  rclcpp
  rclcpp_action
  plansys2_executor
  plansys2_msgs
  geometry_msgs
  nav2_msgs
  nav_msgs           
  sensor_msgs          
  tf2                
  tf2_geometry_msgs
  cv_bridge        
  image_transport
)

ament_target_dependencies(process_last_image_action_node
  rclcpp
  rclcpp_action
  plansys2_executor
  plansys2_msgs
  geometry_msgs
  nav2_msgs
  sensor_msgs        
  cv_bridge       
  yaml-cpp
  ament_index_cpp
)

# Make executables depend on message generation
rosidl_get_typesupport_target(cpp_typesupport_target
  ${PROJECT_NAME} rosidl_typesupport_cpp)

target_link_libraries(get_plan
  "${cpp_typesupport_target}"
)

target_link_libraries(get_plan_and_execute
  "${cpp_typesupport_target}"
)

target_link_libraries(move_action_node
  yaml-cpp
  "${cpp_typesupport_target}"
)

target_link_libraries(take_picture_action_node
  ${OpenCV_LIBRARIES}
  "${cpp_typesupport_target}"
)

target_link_libraries(detect_marker_action_node
  ${OpenCV_LIBRARIES}
  "${cpp_typesupport_target}"
)

target_link_libraries(process_last_image_action_node
  ${OpenCV_LIBRARIES}
  "${cpp_typesupport_target}"
)

install(DIRECTORY
  launch
  msg
  urdf
  worlds
  rviz
  meshes
  pddl
  maps
  config
  DESTINATION share/${PROJECT_NAME}
)

install(TARGETS
  get_plan get_plan_and_execute move_action_node take_picture_action_node detect_marker_action_node process_last_image_action_node
  DESTINATION lib/${PROJECT_NAME}
)

if(BUILD_TESTING)
  find_package(ament_lint_auto REQUIRED)
  set(ament_cmake_copyright_FOUND TRUE)
  set(ament_cmake_cpplint_FOUND TRUE)
  ament_lint_auto_find_test_dependencies()
endif()

ament_package()