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
    dotNetHelper: null,

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
                    this.dotNetRef.invokeMethodAsync("OnObjectPicked", target.metadata?.name || target.name, target.id);
                }
            }
        };

        // Render Loop
        this.engine.runRenderLoop(() => {
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

    loadProceduralModel: function (jsonData, position = [0, 0, 0], scale = 1, rotation = [0, 0, 0], id = null) {
        if (!this.scene) return;
        const modelData = typeof jsonData === "string" ? JSON.parse(jsonData) : jsonData;
        const finalId = id || (modelData.Name + "_" + Date.now() + "_" + Math.floor(Math.random() * 1000));
        const root = new BABYLON.TransformNode(finalId, this.scene);
        root.id = finalId;
        root.metadata = { isRoot: true, type: modelData.Type, name: modelData.Name };

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

            switch (part.Shape) {
                case "Box":
                    mesh = BABYLON.MeshBuilder.CreateBox(part.Id, options, this.scene);
                    break;
                case "Sphere":
                    mesh = BABYLON.MeshBuilder.CreateSphere(part.Id, options, this.scene);
                    break;
                case "Cylinder":
                    mesh = BABYLON.MeshBuilder.CreateCylinder(part.Id, options, this.scene);
                    break;
                case "Torus":
                    mesh = BABYLON.MeshBuilder.CreateTorus(part.Id, options, this.scene);
                    break;
                case "Cone":
                    mesh = BABYLON.MeshBuilder.CreateCylinder(part.Id, { ...options, diameterTop: 0 }, this.scene);
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

                const material = new BABYLON.StandardMaterial("mat_" + part.Id, this.scene);
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
                    const actualTarget = root.getChildMeshes().find(m => m.name === entry.TargetId);
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

        return finalId;
    },

    loadModels: function (models) {
        if (!this.scene || !models) return;
        models.forEach(m => {
            this.loadProceduralModel(m.jsonData, m.position, m.scale, m.rotation, m.id);
        });
    },

    spawnAsteroidField: function (jsonData, count, radius) {
        for (let i = 0; i < count; i++) {
            const angle = Math.random() * Math.PI * 2;
            const dist = radius + Math.random() * radius;
            const x = Math.cos(angle) * dist;
            const z = Math.sin(angle) * dist;
            const y = (Math.random() - 0.5) * 100;
            const scale = 5 + Math.random() * 15;
            const rot = [Math.random() * 360, Math.random() * 360, Math.random() * 360];
            this.loadProceduralModel(jsonData, [x, y, z], scale, rot);
        }
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
            if (this.dotNetRef) this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id);
            return;
        }

        const frameCount = 60 * durationSec;

        if (isNaN(frameCount) || frameCount <= 0) {
            node.position = target;
            if (this.dotNetRef) this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id);
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
        return [node.position.x, node.position.y, node.position.z];
    },

    getRadarData: function () {
        if (!this.scene) return [];
        const nodes = this.scene.getNodes().filter(n => n.metadata && n.metadata.isRoot && n.isEnabled());
        return nodes.map(n => ({
            id: n.id,
            name: n.metadata.name,
            type: n.metadata.type,
            pos: [n.position.x, n.position.z]
        }));
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
        } else {
            node.dispose();
        }
    },

    setSurfaceView: function (planetId) {
        if (!this.scene) return;
        this.isSurfaceView = true;
        this.currentPlanetId = planetId;

        // 0. Environment Setup (Deep Detail Clarity)
        this.scene.clearColor = new BABYLON.Color4(0.6, 0.8, 1.0, 1.0);
        this.scene.ambientColor = new BABYLON.Color3(0.2, 0.2, 0.2); // Further reduced to bring back building detail
        this.scene.fogMode = BABYLON.Scene.FOGMODE_NONE;

        // Add Sky Light (Ambient Fill)
        let skyLight = this.scene.getLightByName("skyLight");
        if (!skyLight) {
            skyLight = new BABYLON.HemisphericLight("skyLight", new BABYLON.Vector3(0, 1, 0), this.scene);
        }
        skyLight.intensity = 0.7;
        skyLight.diffuse = new BABYLON.Color3(1, 1, 1);
        skyLight.groundColor = new BABYLON.Color3(0.5, 0.4, 0.3);

        // Add Sun Light
        let sunLight = this.scene.getLightByName("sunLight");
        if (!sunLight) {
            sunLight = new BABYLON.DirectionalLight("sunLight", new BABYLON.Vector3(0.1, -1, 0.1), this.scene);
        }
        sunLight.intensity = 0.9;

        // 0.1 Find target position
        const hubNode = this.scene.getNodeById("Hub_" + planetId);
        const planetNode = this.scene.getNodeById(planetId);
        const targetNode = hubNode || planetNode;
        const targetPos = targetNode ? targetNode.absolutePosition.clone() : BABYLON.Vector3.Zero();

        // 1. Hide Space Objects
        this.scene.getNodes().forEach(node => {
            if (node.id === "terrain") return;
            if (node instanceof BABYLON.Light) return;
            if (node.metadata && node.metadata.isRoot) {
                const isHub = node.id.startsWith("Hub_" + planetId);
                const isBuilding = node.id.startsWith("ColonyBuilding_" + planetId);
                node.setEnabled(isHub || isBuilding);
            }
        });

        // 2. Setup Terrain (Fail-Safe "Sandy Beach")
        if (!this.terrain) {
            this.terrain = BABYLON.MeshBuilder.CreateGround("terrain", { width: 10000, height: 10000, subdivisions: 2 }, this.scene);
            const terrainMat = new BABYLON.StandardMaterial("terrainMat", this.scene);

            // FORCE VISIBILITY: Use pure emissive, unlit color.
            // This ground will stay bright even if there are ZERO lights in the scene.
            terrainMat.diffuseColor = new BABYLON.Color3(0, 0, 0); // Ignore diffuse
            terrainMat.specularColor = new BABYLON.Color3(0, 0, 0);
            terrainMat.emissiveColor = new BABYLON.Color3(0.85, 0.8, 0.65); // Slightly lowered but still bright
            terrainMat.disableLighting = true; // Make it independent of lights

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

    startPlacement: function (json, dotNetHelper) {
        this.dotNetHelper = dotNetHelper;
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
                    this.dotNetHelper.invokeMethodAsync('FinalizePlacement', pos);
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
    }
};
