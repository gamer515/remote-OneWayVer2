public interface ISaveSystem 
{
    void Save<T>(string category, T data);
    T Load<T>(string category);
    bool Exists(string category);
    string[] GetAllSaveFiles();
    void DeleteAllSaves();
}
