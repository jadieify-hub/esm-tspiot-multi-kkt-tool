namespace EsmTspiot.Shared.Models
{
    public sealed class TcpListenerSnapshotItem
    {
        public TcpListenerSnapshotItem()
        {
        }

        public TcpListenerSnapshotItem(int port, string ownerServiceName, bool isOwnerVerified)
        {
            Port = port;
            OwnerServiceName = ownerServiceName;
            IsOwnerVerified = isOwnerVerified;
        }

        public int Port { get; set; }
        public string OwnerServiceName { get; set; }
        public bool IsOwnerVerified { get; set; }
    }
}
