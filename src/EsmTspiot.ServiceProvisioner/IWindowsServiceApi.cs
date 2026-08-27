namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsServiceApi
    {
        WindowsServiceRecord Query(string serviceName);
        void Create(WindowsServiceDefinition definition);
        void Update(WindowsServiceDefinition definition);
        void Start(string serviceName);
        void RequestStop(string serviceName);
        void Delete(string serviceName);
    }
}
