using UnityEngine;

public interface IJsonSerializer
{
    void SaveData<T>(T data, string fileName);

    T LoadData<T>(string fileName);
}
