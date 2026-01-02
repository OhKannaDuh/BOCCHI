using Ocelot.Lifecycle;
using Ocelot.Services.Translation;

namespace BOCCHI;

public class TranslationLoader(ITranslationRepository translations) : IOnStart
{
    public void OnStart()
    {
        translations.LoadFromDirectory("Translations", "en");
    }
}
