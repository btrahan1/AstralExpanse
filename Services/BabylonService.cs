using Microsoft.JSInterop;
using System.Collections.Generic;

namespace AstralExpanse.Services;

public class BabylonService
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<BabylonService>? _objRef;
    private TaskCompletionSource<float[]?>? _placementTcs;

    public event Action<string, string, string>? OnObjectClicked;

    public BabylonService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeEngine(string canvasId)
    {
        _objRef = DotNetObjectReference.Create(this);
        await _jsRuntime.InvokeVoidAsync("AstralEngine.init", canvasId, _objRef);
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
    public void OnObjectPicked(string type, string name, string id)
    {
        OnObjectClicked?.Invoke(type, name, id);
    }

    public async Task<float[]?> StartPlacement(string json)
    {
        _objRef ??= DotNetObjectReference.Create(this);
        _placementTcs = new TaskCompletionSource<float[]?>();
        await _jsRuntime.InvokeVoidAsync("AstralEngine.startPlacement", json, _objRef);
        return await _placementTcs.Task;
    }

    public async Task CancelPlacement()
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.cancelPlacement");
        _placementTcs?.TrySetResult(null);
    }

    [JSInvokable]
    public void FinalizePlacement(float[] position)
    {
        _placementTcs?.TrySetResult(position);
    }

    public async Task<string> SpawnRover(string json, float[] position)
    {
        return await _jsRuntime.InvokeAsync<string>("AstralEngine.spawnRover", json, position);
    }

    public async Task UpdateRoverInput(string id, Dictionary<string, bool> input)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.updateRoverInput", id, input);
    }

    public async Task SetRoverActive(string id, bool active)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.setRoverActive", id, active);
    }

    public async Task UpdateConstructionProgress(string id, string type, float progress, float[] position)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.updateConstructionProgress", id, type, progress, position);
    }

    public async Task RemoveConstructionProgress(string id)
    {
        await _jsRuntime.InvokeVoidAsync("AstralEngine.removeConstructionProgress", id);
    }

    public void Dispose()
    {
        _objRef?.Dispose();
    }
}
