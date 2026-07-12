using System.IO;
using UnityEngine;

public class SaveManager : ISaveSystem
{
    // 저장 파일이 위치할 기본 경로
    private string BasePath => Path.Combine(Application.persistentDataPath, "Saves");

    public SaveManager() 
    {
        if (!Directory.Exists(BasePath))
        {
            Directory.CreateDirectory(BasePath);
        }
    }

    public void Save<T>(string category, T data) 
    {
        string path = Path.Combine(BasePath, $"{category}.json");
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
    }

    public T Load<T>(string category) 
    {
        string path = Path.Combine(BasePath, $"{category}.json");
        if (!File.Exists(path)) return default;

        string json = File.ReadAllText(path);
        return JsonUtility.FromJson<T>(json);
    }

    public bool Exists(string category) 
    {
        return File.Exists(Path.Combine(BasePath, $"{category}.json"));
    }

    public string[] GetAllSaveFiles() 
    {
        if (!Directory.Exists(BasePath)) return new string[0];
        
        string[] files = Directory.GetFiles(BasePath, "*.json");
        for (int i = 0; i < files.Length; i++) 
        {
            files[i] = Path.GetFileNameWithoutExtension(files[i]);
        }
        return files;
    }

    public void DeleteAllSaves()
    {
        if (Directory.Exists(BasePath))
        {
            Directory.Delete(BasePath, true);
            Directory.CreateDirectory(BasePath);
        }
    }
}
