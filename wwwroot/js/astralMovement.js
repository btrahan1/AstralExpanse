Object.assign(window.AstralEngine, {
    moveModel: function (id, targetPos, durationSec) {
        const node = this.scene.getNodeById(id);
        if (!node) {
            console.warn(`AstralEngine: moveModel failed, node not found: ${id}`);
            return;
        }

        const target = new BABYLON.Vector3(targetPos[0], targetPos[1], targetPos[2]);
        const dist = BABYLON.Vector3.Distance(node.position, target);

        if (dist < 0.1) {
            node.position = target;
            if (this.dotNetRef) {
                // Break synchronous recursion loop by yielding to event loop
                setTimeout(() => this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id), 1);
            }
            return;
        }

        const frameCount = 60 * durationSec;

        if (isNaN(frameCount) || frameCount <= 0) {
            node.position = target;
            if (this.dotNetRef) {
                setTimeout(() => this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id), 1);
            }
            return;
        }

        // Position Animation
        const posAnim = new BABYLON.Animation("posAnim", "position", 60, BABYLON.Animation.ANIMATIONTYPE_VECTOR3, BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT);
        posAnim.setKeys([
            { frame: 0, value: node.position.clone() },
            { frame: frameCount, value: target }
        ]);

        // Rotation Animation to look at target
        // Prevent gimbal lock/NaN when looking straight up/down or at same spot
        const diff = target.subtract(node.position);
        if (Math.abs(diff.x) > 0.01 || Math.abs(diff.z) > 0.01) {
            const lookAtMatrix = BABYLON.Matrix.LookAtLH(node.position, target, BABYLON.Vector3.Up());
            const lookAtQuat = BABYLON.Quaternion.FromRotationMatrix(lookAtMatrix.invert());
            const lookAtRot = lookAtQuat.toEulerAngles();

            const rotAnim = new BABYLON.Animation("rotAnim", "rotation", 60, BABYLON.Animation.ANIMATIONTYPE_VECTOR3, BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT);
            rotAnim.setKeys([
                { frame: 0, value: node.rotation.clone() },
                { frame: Math.min(20, frameCount), value: lookAtRot }
            ]);
            node.animations = [posAnim, rotAnim];
        } else {
            node.animations = [posAnim];
        }

        this.scene.beginAnimation(node, 0, frameCount, false, 1, () => {
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id);
            }
        });
    },

    stopModel: function (id) {
        const node = this.scene.getNodeById(id);
        if (node) {
            this.scene.stopAnimation(node);
        }
    },

    attachToParent: function (childId, parentId, offset = [0, 0, 5]) {
        const child = this.scene.getNodeById(childId);
        const parent = this.scene.getNodeById(parentId);
        if (!child || !parent) return;

        child.parent = parent;
        child.position = new BABYLON.Vector3(offset[0], offset[1], offset[2]);
        // Reset rotation relative to parent
        child.rotation = new BABYLON.Vector3(0, 0, 0);
    },

    detachFromParent: function (childId, newPosition = null) {
        const child = this.scene.getNodeById(childId);
        if (!child) return;

        const worldPos = child.absolutePosition.clone();
        child.parent = null;

        if (newPosition) {
            child.position = new BABYLON.Vector3(newPosition[0], newPosition[1], newPosition[2]);
        } else {
            child.position = worldPos;
        }
    },

    updateNPCs: function (dt) {
        // Throttled scan for new NPCs or Fighters (every 2 seconds)
        this._scanTimer = (this._scanTimer || 0) + dt;
        if (this._scanTimer > 2.0) {
            this._scanTimer = 0;
            this.scene.getNodes().forEach(node => {
                if (node.metadata && (node.metadata.type === "NPC" || node.metadata.type === "Ship") && !this.npcShips[node.id]) {
                    const isFighter = node.metadata.unitType === "FighterUnit" || node.metadata.unitType === "ScoutUnit" || node.metadata.type === "NPC";
                    if (isFighter) {
                        this.npcShips[node.id] = { root: node, state: "Patrolling", waitTime: 0, type: node.metadata.type };
                    }
                }
            });
        }

        for (let id in this.npcShips) {
            const ship = this.npcShips[id];
            if (!ship.root || ship.root.isDisposed()) {
                delete this.npcShips[id];
                continue;
            }

            // C# State Integration: Check if ship is in a managed state (Returning/Repairing/Building)
            const metaState = ship.root.metadata ? ship.root.metadata.state : null;
            const isManaged = metaState === "ReturningToStation" || metaState === "Repairing" || metaState === "Building" || metaState === "Managed";

            if (isManaged) {
                ship.state = "Managed"; // Halt autonomous logic
                ship.targetId = null;
                ship.targetPos = null;
                continue;
            } else if (ship.state === "Managed") {
                // EXPLICIT RECOVERY: If C# released the ship (e.g. after repair), resume patrolling
                ship.state = "Patrolling";
            }

            if (ship.state === "Patrolling") {
                // Ensure no C# animations (like MoveModel) are fighting us
                if (this.scene.getAnimationRatio() > 0 && ship.root.animations && ship.root.animations.length > 0) {
                    this.scene.stopAnimation(ship.root);
                }

                if (!ship.targetPos || BABYLON.Vector3.Distance(ship.root.position, ship.targetPos) < 20) {
                    // INSTANT PATROL: Pick waypoint relative to STAR/STATION (0,0,0) immediately
                    const angle = Math.random() * Math.PI * 2;
                    const dist = 200 + Math.random() * 300; // Keep within 200-500 radius
                    // Target is absolute position from center (0,0,0)
                    ship.targetPos = new BABYLON.Vector3(Math.cos(angle) * dist, (Math.random() - 0.5) * 50, Math.sin(angle) * dist);
                    ship.waitTime = 0; // continuous movement
                } else {
                    // Move towards target
                    const diff = ship.targetPos.subtract(ship.root.position);
                    const moveStep = diff.normalize().scale(30 * dt); // Standard Patrol Speed
                    ship.root.position.addInPlace(moveStep);

                    // Look toward target
                    const lookAt = BABYLON.Quaternion.FromEulerAngles(0, Math.atan2(diff.x, diff.z), 0);
                    ship.root.rotationQuaternion = BABYLON.Quaternion.Slerp(ship.root.rotationQuaternion || BABYLON.Quaternion.Identity(), lookAt, 2 * dt);
                }
            } else if (ship.state === "Attacking") {
                const targetNode = this.scene.getNodeById(ship.targetId);
                // KAMIKAZE: Aggressively close distance
                if (targetNode && targetNode.isEnabled() && !targetNode.isDisposed()) {
                    // Cancel any running animations to allow manual pursuit
                    this.scene.stopAnimation(ship.root);

                    const diff = targetNode.absolutePosition.subtract(ship.root.position);
                    const dist = diff.length();

                    if (dist > 1500) {
                        ship.state = "Patrolling";
                        ship.targetId = null;
                        ship.targetPos = null;
                        continue; // Fix: Change return to continue to avoid freezing entire loop
                    }

                    // Look at target with high precision
                    const lookAt = BABYLON.Quaternion.FromEulerAngles(0, Math.atan2(diff.x, diff.z), 0);
                    ship.root.rotationQuaternion = BABYLON.Quaternion.Slerp(ship.root.rotationQuaternion || BABYLON.Quaternion.Identity(), lookAt, 8 * dt);

                    // Aggressive Dive: Close to 100 units at high speed
                    if (dist > 100) {
                        const moveVec = diff.normalize().scale(45 * dt);
                        ship.root.position.addInPlace(moveVec);
                    } else {
                        // Point blank: slow down but keep orbiting/passing
                        ship.root.position.addInPlace(diff.normalize().scale(10 * dt));
                    }
                } else {
                    ship.state = "Patrolling";
                    ship.targetId = null;
                    ship.targetPos = null;
                }
            }

            // Proximity Engagement (Client side visual/state hint)
            if (ship.state === "Patrolling") {
                this.scene.getNodes().forEach(node => {
                    if (node.metadata && node.metadata.type === "Ship" && node.isEnabled()) {
                        // FACTION CHECK: Only attack DIFFERENT factions (and ignore same faction)
                        const myFaction = ship.root.metadata.faction || "Player"; // Default to Player
                        const targetFaction = node.metadata.faction || "Player"; // Default to Player

                        // If factions are different, engage
                        if (myFaction !== targetFaction) {
                            const dist = BABYLON.Vector3.Distance(ship.root.position, node.absolutePosition);
                            if (dist < 700) { // Matched to C# Firing Range
                                ship.state = "Attacking";
                                ship.targetId = node.id;
                            }
                        }
                    }
                });
            }
        }
    }
});
