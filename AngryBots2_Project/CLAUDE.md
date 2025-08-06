# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is AngryBots2, a Unity3D third-person shooter game where players battle against robotic enemies. The game features player movement, shooting mechanics, enemy AI, and various visual effects.

## Code Architecture

### Core Systems

1. **Player System**
   - `PlayerMovementAndLook.cs`: Handles player movement and mouse-look rotation
   - `PlayerProjectileSpawner.cs`: Manages player weapon firing and bullet spawning

2. **Projectile System**
   - `ProjectileBehaviour.cs`: Controls bullet movement, lifetime, and collision detection
   - `SimpleBullet.cs`: Basic bullet implementation with linear movement
   - `SeekerBullet.cs`: Smart bullet that seeks toward targets

3. **Enemy System**
   - `EnemyPatrol.cs`: Enemy spider patrol behavior along predefined waypoints
   - `EnemyHealth.cs`: Enemy health management and damage handling
   - Various AI controllers for enemy movement and combat

4. **Weapon System**
   - Multiple weapon scripts implementing different firing behaviors
   - Damage application through the `Health.cs` system

### Key Components

- **Physics**: Uses Unity's physics system for collision detection and movement
- **Navigation**: Utilizes NavMesh for enemy pathfinding
- **Animation**: Uses Unity's Animator system for character animations
- **Particle Effects**: Integrated particle systems for visual feedback
- **Audio**: AudioSource components for sound effects

## Common Development Tasks

### Building and Running
1. Open the project in Unity Hub
2. Select the main scene (AngryBots.unity)
3. Configure build settings in File > Build Settings
4. Click Build and Run to create executable

### Adding New Features
1. Player abilities: Extend `PlayerMovementAndLook.cs` or create new player scripts
2. Enemy types: Create new prefabs with AI behavior scripts
3. Weapons: Implement new projectile behaviors by extending existing bullet classes
4. Levels: Create new scenes and configure NavMesh for enemy navigation

### Modifying Gameplay Mechanics
1. Adjust player movement: Modify values in `PlayerMovementAndLook.cs`
2. Change weapon behavior: Update `PlayerProjectileSpawner.cs` and projectile scripts
3. Tune enemy AI: Modify patrol routes and behavior parameters in enemy scripts
4. Balance difficulty: Adjust health values and damage amounts in respective scripts

## File Organization
- `Assets/Scripts/`: Contains all C# scripts organized by functionality
- `Assets/Prefabs/`: Reusable game objects
- `Assets/Scenes/`: Game scenes
- `Assets/Models/`: 3D models
- `Assets/Materials/`: Surface materials
- `Assets/Textures/`: Image textures
- `Assets/Audio/`: Sound files