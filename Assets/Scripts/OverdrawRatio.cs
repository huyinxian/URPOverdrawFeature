using SceneProfiler.Overdraw.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class OverdrawRatio : MonoBehaviour
{
    private Camera _mainCamera;
    
    private void Start()
    {
        _mainCamera = Camera.main;
    }

    private void OnGUI()
    {
        if (_mainCamera != null)
        {
            var renderer = _mainCamera.GetUniversalAdditionalCameraData().scriptableRenderer as OverdrawRenderer;
            if (renderer != null)
            {
                GUILayout.Label("Overdraw Ratio: " + renderer.GetOverdrawRatio());
            }
        }
    }
}
