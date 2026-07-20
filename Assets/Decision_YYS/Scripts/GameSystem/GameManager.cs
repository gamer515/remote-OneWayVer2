using UnityEngine;
using static Constants;

public class GameManager : MonoBehaviour
{
    private static GameManager instance;

    public static GameManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<GameManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("GameManager");
                    instance = go.AddComponent<GameManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    private GameState currentState;

    private void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); currentState = GameState.Main; }
        else if (instance != this) { Destroy(gameObject); }
    }

    //버튼 누르면 OmnibusData를 받아서 GameManager에 저장하고, GameManager에서 DecisionManager로 전달
    public OmnibusData StartGame()
    {
        return SaveIOService.Instance.LoadData<OmnibusData>("Omnibus_01");
    }
}
