window.AstralEngine = {
    canvas: null,
    engine: null,
    scene: null,
    camera: null,
    dotNetRef: null,
    selectedId: null,

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

        console.log("AstralEngine Initialized 🚀");
    },

    loadProceduralModel: function (jsonData, position = [0, 0, 0], scale = 1, rotation = [0, 0, 0], id = null) {
        if (!this.scene) return;
        const modelData = JSON.parse(jsonData);
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
                mesh.position = new BABYLON.Vector3(part.Position[0], part.Position[1], part.Position[2]);
                mesh.rotation = new BABYLON.Vector3(
                    BABYLON.Angle.FromDegrees(part.Rotation[0]).radians(),
                    BABYLON.Angle.FromDegrees(part.Rotation[1]).radians(),
                    BABYLON.Angle.FromDegrees(part.Rotation[2]).radians()
                );
                mesh.scaling = new BABYLON.Vector3(part.Scale[0], part.Scale[1], part.Scale[2]);

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

        return id;
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
        if (!node) return;

        const target = new BABYLON.Vector3(targetPos[0], targetPos[1], targetPos[2]);
        const frameCount = 60 * durationSec;

        // Position Animation
        const posAnim = new BABYLON.Animation("posAnim", "position", 60, BABYLON.Animation.ANIMATIONTYPE_VECTOR3, BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT);
        posAnim.setKeys([
            { frame: 0, value: node.position.clone() },
            { frame: frameCount, value: target }
        ]);

        // Rotation Animation to look at target
        const lookAtMatrix = BABYLON.Matrix.LookAtLH(node.position, target, BABYLON.Vector3.Up());
        const lookAtQuat = BABYLON.Quaternion.FromRotationMatrix(lookAtMatrix.invert());
        const lookAtRot = lookAtQuat.toEulerAngles();

        const rotAnim = new BABYLON.Animation("rotAnim", "rotation", 60, BABYLON.Animation.ANIMATIONTYPE_VECTOR3, BABYLON.Animation.ANIMATIONLOOPMODE_CONSTANT);
        rotAnim.setKeys([
            { frame: 0, value: node.rotation.clone() },
            { frame: Math.min(20, frameCount), value: lookAtRot }
        ]);

        node.animations = [posAnim, rotAnim];
        this.scene.beginAnimation(node, 0, frameCount, false, 1, () => {
            if (this.dotNetRef) {
                this.dotNetRef.invokeMethodAsync("NotifyMoveComplete", id);
            }
        });
    },

    getModelPosition: function (id) {
        const node = this.scene.getNodeById(id);
        if (!node) return null;
        return [node.position.x, node.position.y, node.position.z];
    },

    getRadarData: function () {
        if (!this.scene) return [];
        const nodes = this.scene.transformNodes.filter(n => n.metadata && n.metadata.isRoot);
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
    }
};
