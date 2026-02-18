window.AstralEngine = {
    canvas: null,
    engine: null,
    scene: null,
    camera: null,
    dotNetRef: null,
    selectedId: null,
    terrain: null,
    currentPlanetId: null,
    isSurfaceView: false,
    blueprintMesh: null,
    isPlacing: false,
    constructionUI: {}, // id -> { container, bar, text }
    rovers: {}, // id -> { root, velocity, speed, facingAngle, input, isActive, ui }
    monoliths: {}, // id -> { root, isDiscovered }
    surfaceMiners: {}, // id -> { root, targetMonolithPos, colonyPos, state, cargo }
    npcShips: {}, // id -> { root, state, targetPos, waitTime }
    combatUI: {}, // id -> { mesh, bar, texture }
    inputMap: {},

    spawnRover: function (id, jsonData, position) {
        if (!this.scene) return;
        if (this.rovers[id]) {
            // Already exists, just update position if needed
            return id;
        }
        const modelData = typeof jsonData === "string" ? JSON.parse(jsonData) : jsonData;

        // Physics Root
        const root = BABYLON.MeshBuilder.CreateBox(id, { size: 1 }, this.scene);
        root.position = new BABYLON.Vector3(position[0], position[1] + 2, position[2]);
        root.isVisible = false;
        root.metadata = { isRoot: true, type: "Rover", name: modelData.Name };

        // Load Visuals
        const visualsId = this.loadProceduralModel(jsonData, [0, 0, 0], 2.5, [0, 0, 0], id + "_visuals");
        const visualsNode = this.scene.getNodeById(visualsId);
        if (visualsNode) visualsNode.parent = root;

        // Interaction UI (Drive Button)
        const plane = BABYLON.MeshBuilder.CreatePlane("ui_" + id, { width: 4, height: 1.5 }, this.scene);
        plane.position = new BABYLON.Vector3(0, 5, 0);
        plane.parent = root;
        plane.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;

        const adt = BABYLON.GUI.AdvancedDynamicTexture.CreateForMesh(plane, 512, 128);
        const btn = BABYLON.GUI.Button.CreateSimpleButton("btn_" + id, "DRIVE ROVER");
        btn.width = "100%";
        btn.height = "100%";
        btn.color = "white";
        btn.background = "#4488ff";
        btn.fontSize = 48;
        btn.onPointerUpObservable.add(() => {
            if (this.dotNetRef) this.dotNetRef.invokeMethodAsync("OnObjectPicked", "Rover", modelData.Name, id);
        });
        adt.addControl(btn);

        this.rovers[id] = {
            root: root,
            visuals: visualsNode,
            velocity: new BABYLON.Vector3(0, 0, 0),
            speed: 0,
            facingAngle: 0,
            input: {},
            isActive: false,
            ui: plane
        };

        return id;
    },

    updateRoverInput: function (id, input) {
        if (this.rovers[id]) {
            this.rovers[id].input = input;
        }
    },

    setRoverActive: function (id, active) {
        const rover = this.rovers[id];
        if (rover) {
            rover.isActive = active;
            rover.ui.setEnabled(!active);
            if (active) {
                this.camera.lockedTarget = rover.root;
                this.camera.radius = 20;
                this.camera.alpha = -Math.PI / 2;
                this.camera.beta = Math.PI / 3;
                this.inputMap = {}; // Reset input on entry
            } else {
                this.camera.lockedTarget = null;
                this.camera.radius = 100;
            }
        }
    },

    updateRovers: function (dt) {
        for (let id in this.rovers) {
            const rover = this.rovers[id];
            if (!rover.isActive) continue;

            const input = this.inputMap || {};
            const speedRatio = 1.0;
            const baseSpeed = 40 * speedRatio;
            const baseAccel = 25 * speedRatio;
            const turnRate = 3.0;

            let throttle = 0;
            let steer = 0;

            // Autodrive Logic
            if (rover.autodrive && rover.target) {
                const targetVec = new BABYLON.Vector3(rover.target[0], rover.root.position.y, rover.target[2]);
                const diff = targetVec.subtract(rover.root.position);
                const dist = diff.length();

                if (dist > 5) {
                    const targetAngle = Math.atan2(diff.x, diff.z);
                    rover.facingAngle = BABYLON.Scalar.LerpAngle(rover.facingAngle, targetAngle, 2.0 * dt);
                    throttle = 0.8; // Moving at steady speed
                } else {
                    rover.autodrive = false;
                    throttle = 0;
                    if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('NotifyAutodriveComplete', id);
                }
            } else {
                if (input["w"] || input["ArrowUp"]) throttle = 1;
                if (input["s"] || input["ArrowDown"]) throttle = -0.5;
                if (input["a"] || input["ArrowLeft"]) steer = -1;
                if (input["d"] || input["ArrowRight"]) steer = 1;
            }

            // Acceleration
            if (throttle !== 0) {
                rover.speed += throttle * baseAccel * dt;
            } else {
                rover.speed = BABYLON.Scalar.Lerp(rover.speed, 0, 2.0 * dt);
                if (Math.abs(rover.speed) < 0.1) rover.speed = 0;
            }

            // Cap Speed
            if (rover.speed > baseSpeed) rover.speed = baseSpeed;
            if (rover.speed < -15) rover.speed = -15;

            // Turning
            if (Math.abs(rover.speed) > 0.5) {
                const turnFactor = throttle < 0 ? -1 : 1;
                rover.facingAngle += steer * turnRate * dt * turnFactor;
            }

            rover.root.rotation.y = rover.facingAngle;

            // Drift / Traction
            const forwardDir = new BABYLON.Vector3(Math.sin(rover.facingAngle), 0, Math.cos(rover.facingAngle));
            const targetVel = forwardDir.scale(rover.speed);
            rover.velocity = BABYLON.Vector3.Lerp(rover.velocity, targetVel, 5.0 * dt);

            // Move
            rover.root.position.addInPlace(rover.velocity.scale(dt));

            // Ground Clamp
            const groundH = this.getHeightAt ? this.getHeightAt(rover.root.position.x, rover.root.position.z) : 0;
            const targetY = groundH + 0.1;
            rover.root.position.y = BABYLON.Scalar.Lerp(rover.root.position.y, targetY, 15.0 * dt);

            // Visual Tilt
            const nextPos = rover.root.position.add(forwardDir.scale(1.0));
            const nextH = this.getHeightAt ? this.getHeightAt(nextPos.x, nextPos.z) : groundH;
            const pitch = -Math.atan2(nextH - groundH, 1.0);
            if (rover.visuals && rover.visuals.rotation) {
                rover.visuals.rotation.x = BABYLON.Scalar.Lerp(rover.visuals.rotation.x, pitch, 5 * dt);
            }

            // Monolith Discovery Check
            if (this.isSurfaceView && this.dotNetRef) {
                for (let mId in this.monoliths) {
                    const monolith = this.monoliths[mId];
                    if (!monolith.isDiscovered) {
                        const dist = BABYLON.Vector3.Distance(rover.root.position, monolith.pos);
                        if (dist < 40) { // Discovery range
                            monolith.isDiscovered = true;
                            this.dotNetRef.invokeMethodAsync('NotifyMonolithDiscovered', mId);
                            this.loadProceduralModel(monolith.jsonData, [monolith.pos.x, monolith.pos.y, monolith.pos.z], 1.0, [0, 0, 0], mId);
                        }
                    }
                }
            }
        }
    },

    registerMonolith: function (id, position, isDiscovered, jsonData) {
        this.monoliths[id] = {
            pos: new BABYLON.Vector3(position[0], position[1], position[2]),
            isDiscovered: isDiscovered,
            jsonData: jsonData
        };
        if (isDiscovered) {
            this.loadProceduralModel(jsonData, position, 1.0, [0, 0, 0], id);
        }
    },

    spawnSurfaceMiner: function (id, position, targetMonolithPos, colonyPos, jsonData) {
        if (!this.scene) return;
        this.loadProceduralModel(jsonData, position, 1.5, [0, 0, 0], id);
        const node = this.scene.getNodeById(id);
        this.surfaceMiners[id] = {
            node: node,
            targetMonolithPos: new BABYLON.Vector3(targetMonolithPos[0], targetMonolithPos[1], targetMonolithPos[2]),
            colonyPos: new BABYLON.Vector3(colonyPos[0], colonyPos[1], colonyPos[2]),
            state: "MovingToMonolith",
            cargo: 0
        };
    },

    updateSurfaceMiners: function (dt) {
        for (let id in this.surfaceMiners) {
            const miner = this.surfaceMiners[id];
            if (!miner.node) continue;

            const target = miner.state === "MovingToMonolith" ? miner.targetMonolithPos : miner.colonyPos;
            const dist = BABYLON.Vector3.Distance(miner.node.position, target);

            if (dist < 2.0) {
                if (miner.state === "MovingToMonolith") {
                    miner.state = "Mining";
                    setTimeout(() => { miner.state = "ReturningToColony"; miner.cargo = 100; }, 3000);
                } else if (miner.state === "ReturningToColony") {
                    miner.state = "Unloading";
                    if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('NotifyMinerUnloaded', id, miner.cargo);
                    setTimeout(() => { miner.state = "MovingToMonolith"; miner.cargo = 0; }, 2000);
                }
            } else if (miner.state === "MovingToMonolith" || miner.state === "ReturningToColony") {
                const dir = target.subtract(miner.node.position).normalize();
                miner.node.position.addInPlace(dir.scale(15 * dt));

                // Ground Clamp
                const groundH = this.getHeightAt ? this.getHeightAt(miner.node.position.x, miner.node.position.z) : miner.node.position.y;
                miner.node.position.y = BABYLON.Scalar.Lerp(miner.node.position.y, groundH + 0.1, 10 * dt);

                // Rotate to face travel
                if (dir.length() > 0.01) {
                    const targetAngle = Math.atan2(dir.x, dir.z);
                    miner.node.rotation.y = BABYLON.Scalar.LerpAngle(miner.node.rotation.y, targetAngle, 5 * dt);
                }
            }
        }
    },

    getHeightAt: function (x, z) {
        if (!this.terrain) return 0;
        return this.terrain.position.y + 0.2;
    },

    updateConstructionProgress: function (id, type, progress, position) {
        if (!this.scene || !BABYLON.GUI) return;

        let ui = this.constructionUI[id];
        if (!ui) {
            // Create New World UI for construction
            const plane = BABYLON.MeshBuilder.CreatePlane("ui_" + id, { width: 25, height: 10 }, this.scene);
            plane.position = new BABYLON.Vector3(position[0], position[1] + 15, position[2]);
            plane.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
            plane.isPickable = false;

            const advancedTexture = BABYLON.GUI.AdvancedDynamicTexture.CreateForMesh(plane, 512 * 5, 256 * 5);

            const container = new BABYLON.GUI.Rectangle();
            container.width = "90%";
            container.height = "80%";
            container.cornerRadius = 30;
            container.color = "white";
            container.thickness = 10;
            container.background = "rgba(0, 0, 0, 0.6)";
            advancedTexture.addControl(container);

            const stack = new BABYLON.GUI.StackPanel();
            container.addControl(stack);

            const text = new BABYLON.GUI.TextBlock();
            text.text = "CONSTRUCTING: " + type.toUpperCase();
            text.color = "white";
            text.fontSize = 120;
            text.height = "200px";
            stack.addControl(text);

            const barBg = new BABYLON.GUI.Rectangle();
            barBg.width = "80%";
            barBg.height = "150px";
            barBg.background = "rgba(255, 255, 255, 0.2)";
            barBg.cornerRadius = 25;
            stack.addControl(barBg);

            const barFill = new BABYLON.GUI.Rectangle();
            barFill.width = "0%";
            barFill.height = "100%";
            barFill.background = "#ffcc00";
            barFill.horizontalAlignment = BABYLON.GUI.Control.HORIZONTAL_ALIGN_LEFT;
            barFill.cornerRadius = 5;
            barBg.addControl(barFill);

            ui = { mesh: plane, bar: barFill, advancedTexture: advancedTexture };
            this.constructionUI[id] = ui;
        }

        // Update existing UI
        ui.bar.width = (progress * 100) + "%";
    },

    removeConstructionProgress: function (id) {
        const ui = this.constructionUI[id];
        if (ui) {
            ui.advancedTexture.dispose();
            ui.mesh.dispose();
            delete this.constructionUI[id];
        }
    },

    init: function (canvasId, dotNetRef) {
        this.canvas = document.getElementById(canvasId);
        this.dotNetRef = dotNetRef;
        if (!this.canvas) {
            console.error("Canvas not found!");
            return;
        }

        this.engine = new BABYLON.Engine(this.canvas, true);
        this.scene = new BABYLON.Scene(this.engine);
        this.scene.clearColor = new BABYLON.Color4(0.01, 0.01, 0.03, 1);

        // Camera
        this.camera = new BABYLON.ArcRotateCamera("camera", -Math.PI / 2, Math.PI / 2.5, 300, BABYLON.Vector3.Zero(), this.scene);
        this.camera.attachControl(this.canvas, true);
        this.camera.lowerRadiusLimit = 10;
        this.camera.upperRadiusLimit = 2000;

        // Lights
        const light = new BABYLON.HemisphericLight("light", new BABYLON.Vector3(0, 1, 0), this.scene);
        light.intensity = 0.5;

        const directionalLight = new BABYLON.DirectionalLight("dirLight", new BABYLON.Vector3(50, 100, 50), this.scene);
        directionalLight.direction = new BABYLON.Vector3(-1, -2, -1);
        directionalLight.intensity = 0.8;

        // Reset
        this.rovers = {};
        this.monoliths = {};
        this.surfaceMiners = {};
        this.npcShips = {};
        this.constructionUI = {};
        this.inputMap = {};

        // Input Handling
        this.scene.actionManager = new BABYLON.ActionManager(this.scene);
        this.scene.actionManager.registerAction(new BABYLON.ExecuteCodeAction(BABYLON.ActionManager.OnKeyDownTrigger, (evt) => {
            let key = evt.sourceEvent.key.toLowerCase();
            this.inputMap[key] = true;
        }));
        this.scene.actionManager.registerAction(new BABYLON.ExecuteCodeAction(BABYLON.ActionManager.OnKeyUpTrigger, (evt) => {
            let key = evt.sourceEvent.key.toLowerCase();
            this.inputMap[key] = false;
        }));

        // Selection Highlight Layer
        this.hl = new BABYLON.HighlightLayer("hl1", this.scene);

        // Picking Logic
        this.scene.onPointerDown = (evt, pickResult) => {
            if (pickResult.hit && pickResult.pickedMesh) {
                let mesh = pickResult.pickedMesh;
                let target = mesh;
                while (target.parent && !target.metadata?.isRoot) {
                    target = target.parent;
                }

                if (this.dotNetRef) {
                    const type = target.metadata?.type || "Unknown";
                    const name = target.metadata?.name || target.name;
                    this.dotNetRef.invokeMethodAsync("OnObjectPicked", type, name, target.id);
                }
            }
        };

        // Render Loop
        this.engine.runRenderLoop(() => {
            const dt = this.engine.getDeltaTime() / 1000;
            this.updateRovers(dt);
            this.updateSurfaceMiners(dt);
            this.updateNPCs(dt);
            this.scene.render();
        });

        // Resize
        window.addEventListener("resize", () => {
            this.engine.resize();
        });

        // Global Key Listener for ESC
        window.addEventListener("keydown", (e) => {
            if (e.key === "Escape" && this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync("NotifyEscapePressed");
            }
        });

        console.log("AstralEngine Initialized 🚀");
    },

    clearScene: function () {
        if (!this.scene) return;

        // Dispose all root meshes
        this.scene.getNodes().forEach(node => {
            if (node.metadata && node.metadata.isRoot) {
                node.dispose();
            }
        });

        // Clear registries
        this.rovers = {};
        this.monoliths = {};
        this.surfaceMiners = {};
        this.npcShips = {};

        // Clear construction UI
        for (let id in this.constructionUI) {
            this.removeConstructionProgress(id);
        }
        this.constructionUI = {};

        for (let id in this.combatUI) {
            this.removeCombatUI(id);
        }
        this.combatUI = {};

        console.log("AstralEngine Scene Cleared 🧹");
    },

    loadProceduralModel: function (jsonData, position = [0, 0, 0], scale = 1, rotation = [0, 0, 0], id = null, additionalMetadata = null) {
        if (!this.scene) return;
        const modelData = typeof jsonData === "string" ? JSON.parse(jsonData) : jsonData;
        const finalId = id || (modelData.Name + "_" + Date.now() + "_" + Math.floor(Math.random() * 1000));
        const root = new BABYLON.TransformNode(finalId, this.scene);
        root.id = finalId;
        root.metadata = { isRoot: true, type: modelData.Type, name: modelData.Name };

        if (additionalMetadata) {
            root.metadata = { ...root.metadata, ...additionalMetadata };
        }

        const pos = position || [0, 0, 0];
        const rot = rotation || [0, 0, 0];

        root.position = new BABYLON.Vector3(pos[0], pos[1], pos[2]);
        root.scaling = new BABYLON.Vector3(scale, scale, scale);
        root.rotation = new BABYLON.Vector3(
            BABYLON.Angle.FromDegrees(rot[0]).radians(),
            BABYLON.Angle.FromDegrees(rot[1]).radians(),
            BABYLON.Angle.FromDegrees(rot[2]).radians()
        );

        modelData.Parts.forEach(part => {
            let mesh;
            const options = this.getMeshOptions(part);
            const partId = finalId + "_" + part.Id;

            switch (part.Shape) {
                case "Box":
                    mesh = BABYLON.MeshBuilder.CreateBox(partId, options, this.scene);
                    break;
                case "Sphere":
                    mesh = BABYLON.MeshBuilder.CreateSphere(partId, options, this.scene);
                    break;
                case "Cylinder":
                    mesh = BABYLON.MeshBuilder.CreateCylinder(partId, options, this.scene);
                    break;
                case "Torus":
                    mesh = BABYLON.MeshBuilder.CreateTorus(partId, options, this.scene);
                    break;
                case "Cone":
                    mesh = BABYLON.MeshBuilder.CreateCylinder(partId, { ...options, diameterTop: 0 }, this.scene);
                    break;
            }

            if (mesh) {
                mesh.parent = root;

                const pPos = part.Position || [0, 0, 0];
                const pRot = part.Rotation || [0, 0, 0];
                const pScale = part.Scale || [1, 1, 1];

                mesh.position = new BABYLON.Vector3(pPos[0], pPos[1], pPos[2]);
                mesh.rotation = new BABYLON.Vector3(
                    BABYLON.Angle.FromDegrees(pRot[0]).radians(),
                    BABYLON.Angle.FromDegrees(pRot[1]).radians(),
                    BABYLON.Angle.FromDegrees(pRot[2]).radians()
                );
                mesh.scaling = new BABYLON.Vector3(pScale[0], pScale[1], pScale[2]);

                const material = new BABYLON.StandardMaterial("mat_" + partId, this.scene);
                material.diffuseColor = BABYLON.Color3.FromHexString(part.ColorHex);

                if (part.Material === "Glass") {
                    material.alpha = 0.4;
                } else if (part.Material === "Glow") {
                    material.emissiveColor = BABYLON.Color3.FromHexString(part.ColorHex);
                }

                mesh.material = material;
                mesh.isPickable = true;
            }
        });

        // Animations
        if (modelData.Timeline && modelData.Timeline.length > 0) {
            modelData.Timeline.forEach(entry => {
                if (entry.Action === "Rotate" && entry.Duration > 0) {
                    const targetPartId = finalId + "_" + entry.TargetId;
                    const actualTarget = root.getChildMeshes().find(m => m.id === targetPartId || m.name === targetPartId);
                    if (actualTarget) {
                        const frameCount = 60 * entry.Duration;
                        const animation = new BABYLON.Animation("rotAnim", "rotation", 60, BABYLON.Animation.ANIMATIONTYPE_VECTOR3, BABYLON.Animation.ANIMATIONLOOPMODE_CYCLE);
                        const keys = [
                            { frame: 0, value: actualTarget.rotation.clone() },
                            {
                                frame: frameCount, value: actualTarget.rotation.add(new BABYLON.Vector3(
                                    BABYLON.Angle.FromDegrees(entry.Value[0]).radians(),
                                    BABYLON.Angle.FromDegrees(entry.Value[1]).radians(),
                                    BABYLON.Angle.FromDegrees(entry.Value[2]).radians()
                                ))
                            }
                        ];
                        animation.setKeys(keys);
                        actualTarget.animations.push(animation);
                        this.scene.beginAnimation(actualTarget, 0, frameCount, true);
                    }
                }
            });
        }

        // Immediate Registration for Always-Moving Doctrine
        if (root.metadata.type === "NPC" || root.metadata.type === "Ship") {
            const isFighter = root.metadata.unitType === "FighterUnit" || root.metadata.type === "NPC";
            if (isFighter) {
                this.npcShips[finalId] = { root: root, state: "Patrolling", waitTime: 0, type: root.metadata.type, spawnPos: root.position.clone() };
            }
        }

        return finalId;
    },

    loadModels: function (models) {
        if (!this.scene || !models) return;
        models.forEach(m => {
            this.loadProceduralModel(m.jsonData, m.position, m.scale, m.rotation, m.id);
        });
    },


    setSelected: function (id) {
        if (this.selectedId) {
            const oldRoot = this.scene.getNodeById(this.selectedId);
            if (oldRoot) {
                oldRoot.getChildMeshes().forEach(m => this.hl.removeMesh(m));
            }
        }

        this.selectedId = id;
        if (id) {
            const newRoot = this.scene.getNodeById(id);
            if (newRoot) {
                newRoot.getChildMeshes().forEach(m => this.hl.addMesh(m, BABYLON.Color3.FromHexString("#4488ff")));
            }
        }
    },

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

    updateModelMetadata: function (id, key, value) {
        const node = this.scene.getNodeById(id);
        if (node) {
            node.metadata = node.metadata || {};
            node.metadata[key] = value;
        }
    },

    fireLaser: function (sourceId, targetId, colorHex = "#ff3300") {
        const source = this.scene.getNodeById(sourceId);
        const target = this.scene.getNodeById(targetId);
        if (!source || !target) return;

        // Sync Target for Kamikaze Pursuit override
        if (this.npcShips[sourceId]) {
            this.npcShips[sourceId].targetId = targetId;
            this.npcShips[sourceId].state = "Attacking";
        }

        const start = source.absolutePosition.clone();
        const end = target.absolutePosition.clone();
        const dist = BABYLON.Vector3.Distance(start, end);

        const laser = BABYLON.MeshBuilder.CreateCylinder("laser_" + Date.now(), {
            height: dist,
            diameter: 0.5,
            tessellation: 4
        }, this.scene);

        laser.position = BABYLON.Vector3.Center(start, end);
        laser.lookAt(end);
        laser.rotation.x += Math.PI / 2;

        const mat = new BABYLON.StandardMaterial("laserMat", this.scene);
        mat.emissiveColor = BABYLON.Color3.FromHexString(colorHex);
        mat.disableLighting = true;
        laser.material = mat;

        // Visual fade out
        let life = 0.2; // seconds
        const anim = setInterval(() => {
            life -= 0.05;
            if (life <= 0) {
                laser.dispose();
                clearInterval(anim);
            } else {
                mat.alpha = life / 0.2;
            }
        }, 50);
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

    getModelPosition: function (id) {
        const node = this.scene.getNodeById(id);
        if (!node) return null;
        return [node.absolutePosition.x, node.absolutePosition.y, node.absolutePosition.z];
    },

    getRadarData: function () {
        if (!this.scene) return [];

        let centerPos = null;
        for (let id in this.rovers) {
            if (this.rovers[id].isActive) {
                centerPos = [this.rovers[id].root.position.x, this.rovers[id].root.position.z];
                break;
            }
        }

        // Filter for root nodes that are enabled
        const nodes = this.scene.getNodes().filter(n =>
            n.metadata &&
            n.metadata.isRoot &&
            n.isEnabled() &&
            !n.id.startsWith("blueprint_") &&
            !n.id.startsWith("ui_")
        );

        return {
            center: centerPos,
            entities: nodes.map(n => ({
                id: n.id,
                name: n.metadata.name || n.name,
                type: n.metadata.type || "Other",
                pos: [n.position.x, n.position.z]
            }))
        };
    },

    getNearestMonolithInfo: function (roverId) {
        const rover = this.rovers[roverId];
        if (!rover) return null;

        let nearestId = null;
        let minDist = Infinity;
        let nearestPos = null;

        for (let mId in this.monoliths) {
            const monolith = this.monoliths[mId];
            if (!monolith.isDiscovered) {
                const dist = BABYLON.Vector3.Distance(rover.root.position, monolith.pos);
                if (dist < minDist) {
                    minDist = dist;
                    nearestId = mId;
                    nearestPos = monolith.pos;
                }
            }
        }

        if (!nearestId) return null;

        const diff = nearestPos.subtract(rover.root.position);
        const bearing = Math.atan2(diff.x, diff.z);

        // Relativize bearing to rover facing
        let relativeBearing = bearing - rover.facingAngle;
        while (relativeBearing > Math.PI) relativeBearing -= Math.PI * 2;
        while (relativeBearing < -Math.PI) relativeBearing += Math.PI * 2;

        return {
            distance: minDist,
            bearing: relativeBearing,
            id: nearestId
        };
    },

    setRoverAutodrive: function (id, active, target = null) {
        const rover = this.rovers[id];
        if (rover) {
            rover.autodrive = active;
            rover.target = target;
        }
    },

    setCameraTarget: function (x, y, z) {
        if (!this.camera) return;
        const targetPos = new BABYLON.Vector3(x, y, z);

        // Ensure we animate the locked target if it exists, or the target property
        const cam = this.camera;
        BABYLON.Animation.CreateAndStartAnimation("camPan", cam, "target", 60, 30, cam.target, targetPos, BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT);
    },

    getMeshOptions: function (part) {
        switch (part.Shape) {
            case "Box": return { width: 1, height: 1, depth: 1 };
            case "Sphere": return { diameter: 1 };
            case "Cylinder": return { diameter: 1, height: 1 };
            case "Torus": return { diameter: 1, thickness: 0.2 };
            case "Cone": return { diameter: 1, height: 1 };
            default: return {};
        }
    },

    focusElement: function (selector) {
        const el = document.querySelector(selector);
        if (el) el.focus();
    },

    destroyModel: function (id, effect = "collapse") {
        const node = this.scene.getNodeById(id);
        if (!node) return;

        if (effect === "collapse") {
            BABYLON.Animation.CreateAndStartAnimation("collapse", node, "scaling", 60, 30, node.scaling.clone(), BABYLON.Vector3.Zero(), BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT, null, () => {
                node.dispose();
            });
        } else if (effect === "explode") {
            const exp = BABYLON.MeshBuilder.CreateSphere("exp_" + id, { diameter: 10 }, this.scene);
            exp.position = node.position.clone();
            const mat = new BABYLON.StandardMaterial("expMat", this.scene);
            mat.emissiveColor = new BABYLON.Color3(1, 0.5, 0);
            exp.material = mat;

            node.dispose();
            this.removeCombatUI(id);

            let s = 1.0;
            const anim = setInterval(() => {
                s += 0.2;
                exp.scaling = new BABYLON.Vector3(s, s, s);
                mat.alpha -= 0.05;
                if (mat.alpha <= 0) {
                    exp.dispose();
                    clearInterval(anim);
                }
            }, 30);
        } else {
            node.dispose();
            this.removeCombatUI(id);
        }

        // Remove from NPC tracking immediately
        if (this.npcShips[id]) {
            delete this.npcShips[id];
        }
    },

    updateCombatUI: function (id, health, maxHealth) {
        if (!this.scene) return;

        // Backup: If health is 0, ensure destruction triggers even if DestroyModel was missed
        if (health <= 0) {
            this.destroyModel(id, "explode");
            return;
        }

        const node = this.scene.getNodeById(id);
        if (!node) return;

        let ui = this.combatUI[id];
        if (!ui) {
            const plane = BABYLON.MeshBuilder.CreatePlane("combat_ui_" + id, { width: 15, height: 3 }, this.scene);
            plane.position = new BABYLON.Vector3(0, 15, 0); // Correct relative offset
            plane.billboardMode = BABYLON.Mesh.BILLBOARDMODE_ALL;
            plane.parent = node;
            plane.isPickable = false;

            const texture = BABYLON.GUI.AdvancedDynamicTexture.CreateForMesh(plane, 256, 64);
            const container = new BABYLON.GUI.Rectangle();
            container.width = "100%";
            container.height = "100%";
            container.background = "rgba(0, 0, 0, 0.4)";
            container.thickness = 2;
            container.color = "white";
            texture.addControl(container);

            const bar = new BABYLON.GUI.Rectangle();
            bar.width = "100%";
            bar.height = "100%";
            bar.background = "#ff4444";
            bar.horizontalAlignment = BABYLON.GUI.Control.HORIZONTAL_ALIGN_LEFT;
            bar.thickness = 0;
            container.addControl(bar);

            ui = { mesh: plane, bar: bar, texture: texture };
            this.combatUI[id] = ui;
        }

        const pct = Math.max(0, health / maxHealth);
        ui.bar.width = (pct * 100) + "%";
        ui.bar.background = pct < 0.3 ? "#ff0000" : (pct < 0.6 ? "#ffaa00" : "#00ff00");
    },

    removeCombatUI: function (id) {
        const ui = this.combatUI[id];
        if (ui) {
            ui.texture.dispose();
            ui.mesh.dispose();
            delete this.combatUI[id];
        }
    },

    setSurfaceView: function (planetId) {
        if (!this.scene) return;
        this.isSurfaceView = true;
        this.currentPlanetId = planetId;

        // 0. Environment Setup (Balanced Detail)
        this.scene.clearColor = new BABYLON.Color4(0.6, 0.8, 1.0, 1.0);
        this.scene.ambientColor = new BABYLON.Color3(0.1, 0.1, 0.1); // Reduced to prevent building washout
        this.scene.fogMode = BABYLON.Scene.FOGMODE_NONE;

        // Add Sky Light (Ambient Fill)
        let skyLight = this.scene.getLightByName("skyLight");
        if (!skyLight) {
            skyLight = new BABYLON.HemisphericLight("skyLight", new BABYLON.Vector3(0, 1, 0), this.scene);
        }
        skyLight.intensity = 0.2;
        skyLight.diffuse = new BABYLON.Color3(1, 1, 1);
        skyLight.groundColor = new BABYLON.Color3(0.5, 0.4, 0.3);

        // Add Sun Light
        let sunLight = this.scene.getLightByName("sunLight");
        if (!sunLight) {
            sunLight = new BABYLON.DirectionalLight("sunLight", new BABYLON.Vector3(0.1, -1, 0.1), this.scene);
        }
        sunLight.intensity = 0.2;

        // 0.1 Find target position
        const hubNode = this.scene.getNodeById("Hub_" + planetId);
        const planetNode = this.scene.getNodeById(planetId);
        const targetNode = hubNode || planetNode;
        const targetPos = targetNode ? targetNode.absolutePosition.clone() : BABYLON.Vector3.Zero();

        this.monoliths = {};
        this.surfaceMiners = {};

        // 1. Hide Space Objects
        this.scene.getNodes().forEach(node => {
            if (node.id === "terrain") return;
            if (node instanceof BABYLON.Light) return;
            if (node.metadata && node.metadata.isRoot) {
                const isHub = node.id.startsWith("Hub_" + planetId);
                const isBuilding = node.id.startsWith("ColonyBuilding_" + planetId);
                const isUnit = node.id.startsWith("Rover") || node.id.startsWith("SurfaceMiner");
                const isMonolith = node.id.startsWith("Monolith");
                node.setEnabled(isHub || isBuilding || isUnit || isMonolith);
            }
        });

        // 2. Setup Terrain (Textured "Sandy Beach")
        if (!this.terrain) {
            this.terrain = BABYLON.MeshBuilder.CreateGround("terrain", { width: 10000, height: 10000, subdivisions: 2 }, this.scene);
            const terrainMat = new BABYLON.StandardMaterial("terrainMat", this.scene);

            // FORCE VISIBILITY + TEXTURE: Use emissive map to avoid "blackout" issues.
            if (BABYLON.NoiseProceduralTexture) {
                const noiseTexture = new BABYLON.NoiseProceduralTexture("noise", 512, this.scene);
                noiseTexture.octaves = 3;
                noiseTexture.persistence = 0.8;
                // High tiling for fine sand grain texture
                noiseTexture.uScale = 60.0;
                noiseTexture.vScale = 60.0;
                // Bright sand palette (Emissive ensures it never goes black)
                noiseTexture.darkColor = new BABYLON.Color3(0.85, 0.8, 0.65);
                noiseTexture.brightColor = new BABYLON.Color3(0.95, 0.9, 0.8);
                noiseTexture.refreshRate = -1; // Static refresh to prevent flicker

                terrainMat.emissiveTexture = noiseTexture;
                terrainMat.emissiveColor = new BABYLON.Color3(0.7, 0.7, 0.7); // Moderated intensity
            } else {
                terrainMat.emissiveColor = new BABYLON.Color3(0.7, 0.7, 0.6);
            }

            terrainMat.diffuseColor = new BABYLON.Color3(0, 0, 0);
            terrainMat.specularColor = new BABYLON.Color3(0, 0, 0);
            terrainMat.disableLighting = true; // Independent of light levels
            terrainMat.backFaceCulling = false;
            this.terrain.material = terrainMat;
        }

        // Position ground precisely
        this.terrain.position.x = targetPos.x;
        this.terrain.position.z = targetPos.z;
        this.terrain.position.y = targetPos.y - 0.2;
        this.terrain.setEnabled(true);

        // 3. Update Camera
        this.camera.setTarget(targetPos);
        this.camera.radius = 80;
        this.camera.alpha = Math.PI / 4;
        this.camera.beta = Math.PI / 3.5;
        this.camera.lowerRadiusLimit = 5;
        this.camera.upperRadiusLimit = 1000;
        this.camera.lowerBetaLimit = 0.1;
        this.camera.upperBetaLimit = Math.PI / 2.1;
    },

    startPlacement: function (json, dotNetRef) {
        if (dotNetRef) this.dotNetRef = dotNetRef;
        this.cancelPlacement();

        try {
            const data = JSON.parse(json);
            const placementId = "blueprint_" + Date.now();
            this.loadProceduralModel(data, [0, -100, 0], 1.0, [0, 0, 0], placementId);
            this.blueprintMesh = this.scene.getNodeById(placementId);

            this.blueprintMesh.getChildMeshes().forEach(m => {
                if (m.material) {
                    m.material = m.material.clone("hologramMat");
                    m.material.alpha = 0.5;
                    m.material.emissiveColor = new BABYLON.Color3(0, 0.5, 1);
                }
            });

            this.isPlacing = true;

            this.scene.onPointerMove = (evt) => {
                if (!this.isPlacing || !this.blueprintMesh) return;
                const pick = this.scene.pick(this.scene.pointerX, this.scene.pointerY, (m) => m.id === "terrain");
                if (pick.hit) {
                    this.blueprintMesh.position = pick.pickedPoint;
                }
            };

            this.scene.onPointerDown = (evt) => {
                if (!this.isPlacing || !this.blueprintMesh) return;
                if (evt.button === 0) { // Left click
                    const pos = [this.blueprintMesh.position.x, this.blueprintMesh.position.y, this.blueprintMesh.position.z];
                    this.isPlacing = false;
                    this.dotNetRef.invokeMethodAsync('FinalizePlacement', pos);
                    this.cancelPlacement();
                }
            };
        } catch (e) {
            console.error("Placement error:", e);
        }
    },

    cancelPlacement: function () {
        this.isPlacing = false;
        if (this.blueprintMesh) {
            this.blueprintMesh.dispose();
            this.blueprintMesh = null;
        }
        this.scene.onPointerMove = null;
        this.scene.onPointerDown = null;
    },

    setSpaceView: function () {
        if (!this.scene) return;
        this.isSurfaceView = false;

        // 0. Reset Environment
        this.scene.clearColor = new BABYLON.Color4(0.01, 0.01, 0.03, 1);
        this.scene.ambientColor = new BABYLON.Color3(0, 0, 0);
        this.scene.fogMode = BABYLON.Scene.FOGMODE_NONE;

        const skyLight = this.scene.getLightByName("skyLight");
        if (skyLight) skyLight.dispose();

        const sunLight = this.scene.getLightByName("sunLight");
        if (sunLight) sunLight.dispose();

        // 1. Show Space Objects
        this.scene.getNodes().forEach(node => {
            if (node.id === "light" || node.id === "dirLight") {
                node.setEnabled(true);
                return;
            }
            if (node.metadata && node.metadata.isRoot) {
                // Buildings are only for surface view
                if (node.id.startsWith("ColonyBuilding_")) {
                    node.setEnabled(false);
                } else {
                    node.setEnabled(true);
                }
            }
        });

        // 2. Hide Terrain
        if (this.terrain) this.terrain.setEnabled(false);

        // 3. Reset Camera
        this.camera.setTarget(BABYLON.Vector3.Zero());
        this.camera.radius = 200;
        this.camera.alpha = -Math.PI / 2;
        this.camera.beta = Math.PI / 3;
        this.camera.lowerRadiusLimit = 50;
        this.camera.upperRadiusLimit = 1000;
        this.camera.lowerBetaLimit = 0.01;
        this.camera.upperBetaLimit = Math.PI - 0.01;
    },

    updateNPCs: function (dt) {
        // Throttled scan for new NPCs or Fighters (every 2 seconds)
        this._scanTimer = (this._scanTimer || 0) + dt;
        if (this._scanTimer > 2.0) {
            this._scanTimer = 0;
            this.scene.getNodes().forEach(node => {
                if (node.metadata && (node.metadata.type === "NPC" || node.metadata.type === "Ship") && !this.npcShips[node.id]) {
                    const isFighter = node.metadata.unitType === "FighterUnit" || node.metadata.type === "NPC";
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
                        // console.log(`NPC ${id} DIVE: Dist=${dist.toFixed(1)} Speed=${moveVec.length().toFixed(2)}`);
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
};
