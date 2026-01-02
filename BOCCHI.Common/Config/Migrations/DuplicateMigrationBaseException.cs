namespace BOCCHI.Common.Config.Migrations;

public class DuplicateMigrationBaseException(int from) : Exception($"Found duplicate from migrator {from}");
