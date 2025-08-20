# Maze Puzzle
3D Maze generator in Unity with graph generated for Pathfinding algorithm.

<img width="2220" height="1233" alt="image" src="https://github.com/user-attachments/assets/1560affa-1441-40ff-bfaa-14130877d07b" />

## Table of contents
* [General info](#general-info)
* [Technologies](#technologies)

## General info
In this Project there are 2 scene.
* **Maze:** to experiment the maze generator and player controller
* **Maze The Game:** play as the player with ability to attack enemies.
<img width="333" height="153" alt="image" src="https://github.com/user-attachments/assets/931f665f-1b4c-4ab2-a579-737cccb23e19" />
<br/>

Prefabs in projects:<br/>
<img width="445" height="155" alt="image" src="https://github.com/user-attachments/assets/51d49abd-ab2a-4cc5-a663-f79fc82cd2fb" />

## Maze Generator & Player Controller
A **customizable Maze Generator** for Unity with many adjustable properties.  
Easily generate optimized mazes for your games or prototypes.

<img width="623" height="1066" alt="image" src="https://github.com/user-attachments/assets/b8d25e1d-fea1-4568-9f31-a39d86468f50" />

### ✨ Features

#### 🏗️ Maze Size
- Set **Width** and **Height** to define the maze dimensions.

#### 🎨 Visuals
- Customizable **Wall Prefab**
- Adjustable **Cell Size**, **Wall Height**, and **Wall Thickness**
- Support for custom **Path Material** and **Floor Material**

#### ⚡ Optimization
- **Wall Pooling** system to minimize `Instantiate` and `Destroy` calls, improving performance when re-generating mazes frequently.
- Configurable **Initial Wall Pool Size**
- Options to **Merge Walls** or use **Points Only At Intersections**
- ✅ It is recommended to enable **Merge Walls** to reduce the number of required objects.

<p align="center">
  <b>Maze without Merged Walls</b> &nbsp;&nbsp;&nbsp;&nbsp; <b>Maze with Merged Walls</b>
</p>
<p align="center">
  <img  width="45%" alt="image" src="https://github.com/user-attachments/assets/5d057417-99eb-4e97-ad7f-6011f62219b2" />
  <img  width="45%" alt="image" src="https://github.com/user-attachments/assets/408efd24-20af-42f1-b72e-667e78fddf20" />
</p>

- ⚠️ You can use **Points Only At Intersections** to reduce the number of graph nodes, but this may cause strange pathfinding behavior (e.g., walking backward or moving through walls) if the player parameters are not configured properly.

<p align="center">
  <b>PointsOnlyAtIntersections: false</b> &nbsp;&nbsp;&nbsp;&nbsp; <b>PointsOnlyAtIntersections: true</b>
</p>
<p align="center">
  <img  width="45%" alt="image" src="https://github.com/user-attachments/assets/408efd24-20af-42f1-b72e-667e78fddf20" />
  <img  width="45% alt="image" src="https://github.com/user-attachments/assets/e6954027-80f5-4bf9-866d-94b4949f99a1" />
</p>

#### ⚙️ Generation
- **Generate On Start** option
- Multiple algorithms (e.g., **Depth First Search**)
- **Seed-based generation** for reproducibility

#### 🎮 Controls
- **Generate Maze** button
- **Clear Children** button

#### 📦 Wall Pooler
- Efficient wall object management
- **Prewarm** option
- **Auto Initialize** support

---

### 🚀 Usage
1. Add the **MazeGenerator** component to a GameObject in your scene.  
2. Configure the properties in the Inspector.  
3. Click **Generate Maze** or enable **Generate On Start** to build a maze automatically.  

---

### 📌 Notes
- Works best with pooled walls for performance.  
- Seed values allow reproducible mazes for testing or level design.  


## Maze The Ganme
Play the scene:
* Spacebar: attack.
* Mouse click: set destination point.

*TODO:*
* Add win/Lose logic
* Add variant enemies
<img width="1288" height="744" alt="image" src="https://github.com/user-attachments/assets/adb3c238-3f98-4f3c-b760-ef65438bbbe3" />

