using UnityEngine;
using UnityEngine.UI;

namespace Ember
{
    /// <summary>One component for every menu button; the action is chosen in the inspector.</summary>
    [RequireComponent(typeof(Button))]
    public class UIButtonAction : MonoBehaviour
    {
        public enum Action { Resume, RestartCheckpoint, MainMenu, Quit, NewGame, Stage1, Stage2, Stage3, ShowPanel, HidePanel, Ending }

        public Action action;
        public GameObject panel;

        void Awake() => GetComponent<Button>().onClick.AddListener(Run);

        public void Run()
        {
            var gm = GameManager.Instance;
            switch (action)
            {
                case Action.Resume: if (gm != null) gm.SetPaused(false); break;
                case Action.RestartCheckpoint: if (gm != null) { gm.SetPaused(false); gm.Respawn(); } break;
                case Action.MainMenu: Load("Title"); break;
                case Action.NewGame: GameManager.ResetSession(); Load("Stage1"); break;
                case Action.Stage1: Load("Stage1"); break;
                case Action.Stage2: Load("Stage2"); break;
                case Action.Stage3: Load("Stage3"); break;
                case Action.Ending: Load("Ending"); break;
                case Action.ShowPanel: if (panel != null) panel.SetActive(true); break;
                case Action.HidePanel: if (panel != null) panel.SetActive(false); break;
                case Action.Quit:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        static void Load(string scene)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.LoadScene(scene, 0.8f);
            else UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }
    }
}
