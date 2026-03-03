# Streaming Virtual Texture (SVT) System

Unity 6000.3.7 compatible SVT system with quadtree-based LOD, terrain integration, and editor tools.

## Features

- **Quadtree-Based LOD** – The virtual address space is subdivided with a quadtree. Each frame the tree is updated against the camera frustum; nodes are split or merged to match the desired screen-space texel density.
- **8K Virtual Texture** – The default configuration uses an 8192 × 8192 virtual texture sampled through a page-table indirection mechanism.
- **GPU Feedback** – A low-resolution feedback pass encodes the visible page address into an offscreen buffer. The buffer is asynchronously read back to the CPU each frame to drive streaming decisions.
- **LRU Physical Cache** – A configurable N × M atlas of physical pages acts as the GPU-side cache. Pages are evicted using a least-recently-used policy.
- **Auto Load / Unload** – Page textures are loaded asynchronously via `Resources.LoadAsync` and automatically unloaded when no longer referenced.
- **Terrain Integration** – Works directly with Unity's `Terrain` component. All terrain layers are baked into a merged albedo + normal texture for SVT capture.
- **Unlimited Layers** – Terrain layers are additively blended at bake time, so any number of layers is supported.
- **Quadtree Mesh Export** – The terrain can be exported as a collection of quadtree mesh patches. Each patch can be rendered independently using the SVT shader, producing the same visual result as the original terrain.
- **Debug System** – An `SVTDebugger` component overlays page load state in the Game/Scene view. The `SVT Editor` window (Window → SVT → SVT Editor) provides runtime statistics and one-click tools.

---

## Directory Layout

```
Assets/SVT/
├── Scripts/
│   ├── SVT.Runtime.asmdef
│   ├── Core/
│   │   ├── SVTSettings.cs          – ScriptableObject: all tunable parameters
│   │   ├── SVTNode.cs              – Quadtree node
│   │   ├── SVTQuadTree.cs          – LOD-driven quadtree
│   │   ├── SVTPageTable.cs         – Indirection texture management
│   │   ├── SVTCache.cs             – Physical texture atlas (LRU)
│   │   ├── SVTTextureManager.cs    – Async page texture loading/unloading
│   │   ├── SVTFeedbackBuffer.cs    – GPU feedback + async readback
│   │   └── SVTManager.cs           – Main MonoBehaviour orchestrator
│   ├── Terrain/
│   │   ├── SVTTerrainIntegration.cs – Bridges Terrain ↔ SVT
│   │   ├── SVTTerrainLayerBaker.cs  – Bakes unlimited terrain layers
│   │   └── SVTTerrainExporter.cs   – Exports terrain as quadtree meshes
│   ├── Rendering/
│   │   └── SVTRenderer.cs          – Feedback pass + material binding
│   └── Debug/
│       └── SVTDebugger.cs          – Gizmos + on-screen HUD
├── Editor/
│   ├── SVT.Editor.asmdef
│   ├── SVTEditorWindow.cs          – Window → SVT → SVT Editor
│   ├── SVTManagerEditor.cs         – Custom Inspector for SVTManager
│   └── SVTTerrainExporterEditor.cs – Custom Inspector for SVTTerrainExporter
├── Shaders/
│   ├── SVTTerrain.shader           – Main SVT terrain surface shader
│   ├── SVTFeedback.shader          – Feedback encoding shader
│   ├── SVTTerrainLayerBlend.shader – Layer bake blend shader
│   └── SVTDebugOverlay.shader      – Debug overlay shader
└── Resources/
    └── (place SVTSettings.asset and page textures here)
```

---

## Quick Start

### 1. Create Settings Asset
1. Right-click in the Project window → **Create → SVT → SVTSettings**.
2. Adjust parameters (virtual texture size, page size, cache size, world size, etc.).
3. Save the asset under `Assets/SVT/Resources/SVTSettings.asset`.

### 2. Set Up the Scene
1. Create an empty GameObject named **SVTManager**.
2. Add the `SVTManager` component and assign the `SVTSettings` asset.
3. Optionally assign a `Camera` (leave blank to use `Camera.main`).
4. Assign the `SVTTerrain` material to the `SVT Material` field.

### 3. Terrain Integration
1. Select the Terrain GameObject.
2. Add `SVTTerrainLayerBaker`, `SVTTerrainIntegration`, and `SVTTerrainExporter` components.
3. Link each component's `SVT Manager` field to the `SVTManager` GameObject.
4. Paint terrain layers normally in the Editor.
5. Open **Window → SVT → SVT Editor** and click **Bake Terrain Layers**.
6. Click **Setup From Terrain** to sync terrain dimensions.

### 4. Export as Quadtree Mesh (Optional)
1. In the SVT Editor window → **Quadtree Mesh Export** section, click **Export Quadtree Mesh Patches**.
2. Each leaf node in the current quadtree becomes a separate mesh patch parented under `SVT_TerrainMeshRoot`.
3. The patches use the `SVTTerrain` shader and match the terrain appearance exactly.

### 5. Debug
1. Add `SVTDebugger` to any GameObject in the scene.
2. Check **Show Overlay** for an on-screen HUD.
3. Check **Draw Gizmos** for coloured node wireframes in the Scene view.
4. Use the **SVT Editor** window for runtime statistics.

---

## Page Texture Naming Convention

Page textures are loaded from Unity's `Resources` system using the following path pattern:

```
SVTPages/mip<N>/page_<X>_<Y>_albedo   → Texture2D (albedo)
SVTPages/mip<N>/page_<X>_<Y>_normal   → Texture2D (normal map)
```

Example for mip 0, page (3, 5):
```
Resources/SVTPages/mip0/page_3_5_albedo.png
Resources/SVTPages/mip0/page_3_5_normal.png
```

You can replace the path-building logic in `SVTManager.BuildPagePath()` to use Addressables or any other asset loading system.

---

## Shader Properties

### SVTTerrain.shader
| Property | Description |
|---|---|
| `_SVT_IndirectionTex` | Indirection texture (RGBA32, point-filtered) |
| `_SVT_AlbedoAtlas` | Physical albedo cache atlas |
| `_SVT_NormalAtlas` | Physical normal cache atlas |
| `_SVT_PageTableSize` | XY = number of pages per axis at mip 0 |
| `_SVT_CacheSize` | XY = number of cache slots per axis |
| `_SVT_PageSize` | Page size in texels |
| `_SVT_WorldRect` | XZ world origin and XZ world size of the virtual texture footprint |

---

## Architecture Overview

```
SVTManager (MonoBehaviour)
  ├── SVTQuadTree          ← LOD decisions per frame
  │     └── SVTNode[]      ← one node per virtual page region
  ├── SVTPageTable         ← indirection texture (GPU)
  ├── SVTCache             ← physical atlas + LRU eviction
  ├── SVTTextureManager    ← async load/unload of page textures
  └── SVTFeedbackBuffer    ← GPU readback of visible pages

SVTTerrainIntegration      ← syncs Terrain ↔ SVTManager
SVTTerrainLayerBaker       ← bakes unlimited terrain layers → merged RT
SVTTerrainExporter         ← exports quadtree mesh patches
SVTRenderer                ← feedback pass rendering
SVTDebugger                ← gizmos + HUD overlay
```
