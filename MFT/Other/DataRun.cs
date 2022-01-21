namespace MFT.Other;

public class DataRun
{
    public DataRun(ulong clustersInRun, long clusterOffset)
    {
        ClustersInRun = clustersInRun;
        ClusterOffset = clusterOffset;
    }

    public ulong ClustersInRun { get; }
    public long ClusterOffset { get; }

    public override string ToString()
    {
        return $"Cluster offset: 0x{ClusterOffset:X}, # clusters: 0x{ClustersInRun:X}";
    }
}