Object.assign(window.AstralEngine, {
    loadProceduralModel: function (jsonData, position = [0, 0, 0], scale = 1, rotation = [0, 0, 0], id = null, additionalMetadata = null) {
        if (!this.scene) return;
        const modelData = typeof jsonData === "string" ? JSON.parse(jsonData) : jsonData;
        const finalId = id || (modelData.Name + "_" + Date.now() + "_" + Math.floor(Math.random() * 1000));

        // Cleanup existing if ID collision
        const existing = this.scene.getNodeById(finalId);
        if (existing) existing.dispose();

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
            const isFighter = root.metadata.unitType === "FighterUnit" || root.metadata.unitType === "ScoutUnit" || root.metadata.type === "NPC";
            if (isFighter) {
                // Determine waitTime or state based on metadata if needed
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
            if (this.removeCombatUI) this.removeCombatUI(id);

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
            if (this.removeCombatUI) this.removeCombatUI(id);
        }

        // Remove from NPC tracking immediately
        if (this.npcShips[id]) {
            delete this.npcShips[id];
        }
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

    updateModelMetadata: function (id, key, value) {
        const node = this.scene.getNodeById(id);
        if (node) {
            node.metadata = node.metadata || {};
            node.metadata[key] = value;
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

    getModelPosition: function (id) {
        const node = this.scene.getNodeById(id);
        if (!node) return null;
        return [node.absolutePosition.x, node.absolutePosition.y, node.absolutePosition.z];
    }
});
