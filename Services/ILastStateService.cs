namespace PortaFile.Services
{
    public interface ILastStateService
    {
        ApplicationLastState Load();
        void Save(ApplicationLastState state);
    }
}
