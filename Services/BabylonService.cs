using Microsoft.JSInterop;
using System.Collections.Generic;

namespace AstralExpanse.Services;

public class BabylonService
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<BabylonService>? _objRef;

    public event Action<string, string>? OnObjectClicked;

    public BabylonService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeEngine(string canvasId)
    {
        _objRef = DotNetObjectReference.Create(this);
        await _jsRuntime.InvokeVoidAsync("AstralEngine.init", canvasId, _objRef);
    }

    public async Task SpawnAsteroidField(string jsonContent, int count, float radius)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.spawnAsteroidField", jsonContent, count, radius);
    }

    public async Task<string> LoadModel(string jsonContent, float[]? pos = null, float scale = 1.0f, float[]? rot = null, string? id = null)
    {
        return await _jsRuntime.InvokeAsync<string>("AstralEngine.loadProceduralModel", jsonContent, pos ?? new float[] { 0, 0, 0 }, scale, rot ?? new float[] { 0, 0, 0 }, id);
    }

    public async Task LoadModels(List<ModelLoadData> models)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.loadModels", models);
    }

    public async Task MoveModel(string id, float[] targetPos, float duration)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.moveModel", id, targetPos, duration);
    }

    public async Task SetSelected(string? id)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setSelected", id);
    }

    public async Task SetSurfaceView(string planetId)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setSurfaceView", planetId);
    }

    public async Task SetSpaceView()
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setSpaceView");
    }

    public async Task AttachToParent(string childId, string parentId, float[]? offset = null)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.attachToParent", childId, parentId, offset ?? new float[] { 0, 0, 5 });
    }

    public async Task DetachFromParent(string childId, float[]? newPosition = null)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.detachFromParent", childId, newPosition);
    }

    public async Task SetCameraTarget(float x, float y, float z)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setCameraTarget", x, y, z);
    }

    public async Task<float[]?> GetModelPosition(string id)
    {
        return await _jsRuntime.InvokeAsync<float[]?>("AstralEngine.getModelPosition", id);
    }

    public async Task<List<RadarEntity>> GetRadarData()
    {
        return await _jsRuntime.InvokeAsync<List<RadarEntity>>("AstralEngine.getRadarData");
    }

    public event Action<string>? OnMoveComplete;

    [JSInvokable]
    public void NotifyMoveComplete(string id)
    {
        OnMoveComplete?.Invoke(id);
    }

    public async Task DestroyModel(string id, string effect = "collapse")
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.destroyModel", id, effect);
    }

    public event Action? OnEscapePressed;

    [JSInvokable]
    public void NotifyEscapePressed()
    {
        OnEscapePressed?.Invoke();
    }

    [JSInvokable]
    public void OnObjectPicked(string name, string id)
    {
        OnObjectClicked?.Invoke(name, id);
    }

    public void Dispose()
    {
        _objRef?.Dispose();
    }
}
