namespace Dqm.Testcontainers.ClickHouse;

internal static class BackupDiskConfigGenerator
{
    public const string DiskName = "backups";
    public const string ContainerBackupPath = "/var/lib/clickhouse/backups/";

    public static string BackupDiskXml() =>
        $"""
         <clickhouse>
           <storage_configuration>
             <disks>
               <{DiskName}>
                 <type>local</type>
                 <path>{ContainerBackupPath}</path>
               </{DiskName}>
             </disks>
           </storage_configuration>
           <backups>
             <allowed_disk>{DiskName}</allowed_disk>
             <allowed_path>{ContainerBackupPath}</allowed_path>
           </backups>
         </clickhouse>
         """;
}
