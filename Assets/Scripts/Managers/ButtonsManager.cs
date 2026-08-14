using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static MajdataViewX.Base.MajCtx;

namespace MajdataViewX.Managers
{
    public class ButtonsManager : MonoBehaviour
    {
        private Dropdown dd;

        private void Awake()
        {
            buttonsManager = this;
        }

        private void Start()
        {
            dd = GameObject.Find("DDResolution").GetComponent<Dropdown>();
            dd.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ToggleFullscreen();
        }

        public void ToggleFullscreen()
        {
            print("ToggleFullScreen");
            var resolutions = Screen.resolutions;
            if (Screen.fullScreen)
            {
                var width = 300;
                var height = 300;
                Screen.SetResolution(width, height, false);
            }
            else
            {
                Screen.SetResolution(resolutions[resolutions.Length - 1].width, resolutions[resolutions.Length - 1].height,
                    true);
            }

            Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        }

        public void DisplayDropdown()
        {
            dd.value = 999;
            dd.gameObject.SetActive(true);
        }

        public void SetResolution()
        {
            var i = dd.value;
            Debug.Log(i);
            switch (i)
            {
                case 0:
                    Screen.SetResolution(512, 512, false);
                    break;
                case 1:
                    Screen.SetResolution(1080, 1080, false);
                    break;
                case 2:
                    Screen.SetResolution(1280, 720, false);
                    break;
                case 3:
                    Screen.SetResolution(1920, 1080, false);
                    break;
                case 4:
                    Screen.SetResolution(2560, 1440, false);
                    break;
                case 5:
                    Screen.SetResolution(3840, 2160, false);
                    break;
            }

            dd.gameObject.SetActive(false);
        }

        public void ToggleCamera()
        {
            if (_playManager.MainCamera.targetDisplay != 0)
            {
                _playManager.MainCamera.targetDisplay = 0;
                _playManager.GameCamera.targetDisplay = 1;
                _inputManager.CurrentCamera = _playManager.MainCamera;
            }
            else
            {
                _playManager.MainCamera.targetDisplay = 1;
                _playManager.GameCamera.targetDisplay = 0;
                _inputManager.CurrentCamera = _playManager.GameCamera;
            }
        }
    }
}