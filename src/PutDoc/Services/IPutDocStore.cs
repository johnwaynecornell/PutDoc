namespace PutDoc.Services;
public interface IPutDocStore
{
    Task<PutDocFile> LoadAsync();
    Task SaveAsync(PutDocFile file);
    string RootPath { get; }
    string PutDocFilePath { get; }
}
