using System.Collections.Generic;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class RoverService
{
    private readonly BabylonService _babylon;
    private readonly GameStateService _gameState;
    private readonly Dictionary<string, bool> _inputMap = new();
    private bool _isDriving = false;
    private string? _currentRoverId;

    public bool IsDriving => _isDriving;

    public RoverService(BabylonService babylon, GameStateService gameState)
    {
        _babylon = babylon;
        _gameState = gameState;

        _babylon.OnObjectClicked += HandleObjectClicked;
    }

    private async void HandleObjectClicked(string type, string name, string id)
    {
        if (type == "Rover")
        {
            await EnterDriveMode(id);
        }
    }

    public async Task EnterDriveMode(string roverId)
    {
        if (_isDriving) return;

        _isDriving = true;
        _currentRoverId = roverId;
        await _babylon.SetRoverActive(roverId, true);
        _gameState.Notify();
    }

    public async Task ExitDriveMode()
    {
        if (!_isDriving || _currentRoverId == null) return;

        _isDriving = false;
        await _babylon.SetRoverActive(_currentRoverId, false);
        
        // Update Game State with final position
        var pos = await _babylon.GetModelPosition(_currentRoverId);
        if (pos != null && _gameState.Planets.Find(p => p.Rover?.Id == _currentRoverId) is var planet && planet?.Rover != null)
        {
            planet.Rover.Position = pos;
        }

        _currentRoverId = null;
        _gameState.Notify();
    }

    public async Task HandleKeyDown(string key)
    {
        if (!_isDriving || _currentRoverId == null) return;
        
        var k = key.ToLower();
        if (!_inputMap.ContainsKey(k) || !_inputMap[k])
        {
            _inputMap[k] = true;
            await _babylon.UpdateRoverInput(_currentRoverId, _inputMap);
        }
    }

    public async Task HandleKeyUp(string key)
    {
        if (!_isDriving || _currentRoverId == null) return;

        var k = key.ToLower();
        if (_inputMap.ContainsKey(k) && _inputMap[k])
        {
            _inputMap[k] = false;
            await _babylon.UpdateRoverInput(_currentRoverId, _inputMap);
        }
    }
}
