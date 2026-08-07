// Scene loading with a simple fade overlay.
//
// Central place for scene transitions so callers (MainMenuController,
// LobbyController, PauseMenuController, GameNetworkManager) don't
// re-implement fade logic. Also keeps scene name constants in one
// place — no magic strings scattered across the UI code.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LifeIsAGame.UI
{
    public sealed class SceneFlow : MonoBehaviour
    {
        public const string MainMenu   = "MainMenu";
        public const string Lobby      = "Lobby";
        public const string Gameplay   = "TheWorldIsAGame";

        public static SceneFlow Instance { get; private set; }

        private Image  _fadeImage;
        private Canvas _canvas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("~SceneFlow");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SceneFlow>();
            Instance.BuildFadeCanvas();
        }

        private void BuildFadeCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32767; // above every gameplay UI
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            var img = new GameObject("Fade").AddComponent<Image>();
            img.transform.SetParent(transform, worldPositionStays: false);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(0f, 0f, 0f, 0f);
            img.raycastTarget = false;
            _fadeImage = img;
        }

        /// <summary>Fade to black, load scene, fade back in.</summary>
        public void LoadScene(string sceneName, float fadeDuration = 0.35f)
        {
            StopAllCoroutines();
            StartCoroutine(LoadRoutine(sceneName, fadeDuration));
        }

        private IEnumerator LoadRoutine(string sceneName, float dur)
        {
            yield return FadeTo(1f, dur);
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            while (!op.isDone) yield return null;
            yield return FadeTo(0f, dur);
        }

        private IEnumerator FadeTo(float targetAlpha, float dur)
        {
            _fadeImage.raycastTarget = true;
            Color c = _fadeImage.color;
            float startA = c.a, elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(startA, targetAlpha, elapsed / dur);
                _fadeImage.color = c;
                yield return null;
            }
            c.a = targetAlpha;
            _fadeImage.color = c;
            _fadeImage.raycastTarget = targetAlpha > 0.99f;
        }
    }
}
