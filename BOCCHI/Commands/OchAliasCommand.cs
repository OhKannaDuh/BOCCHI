using Ocelot.Services.Commands;
using Ocelot.Services.Translation;

namespace BOCCHI.Commands;

public class OchAliasCommand(IMainCommand main, ITranslator<OchAliasCommand> translator) : OcelotCommand(translator)
{
    public override string Command => "och";

    public override List<string> Aliases => ["occultcrescenthelper"];

    public override bool Hidden => true;

    public override void Execute(CommandContext context) => main.Execute(context);
}
