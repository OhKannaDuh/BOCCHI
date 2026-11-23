using System.Reflection;
using BOCCHI.Common;
using BOCCHI.Common.UI;
using Ocelot.Config;
using Ocelot.Config.Fields;
using Xunit;

namespace BOCCHI.Tests;

public class QuickSettingsTests
{
    [Fact]
    public void EveryQuickSettingIsADrawableSetting()
    {
        foreach ((MainWindowSection tab, (Type Config, string Property)[] fields) in QuickSettings.Tabs)
        {
            foreach ((Type config, string property) in fields)
            {
                Assert.True(typeof(IAutoConfig).IsAssignableFrom(config), $"{tab}: {config.Name} is not a settings page");
                PropertyInfo? prop = config.GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
                Assert.True(prop?.GetCustomAttributes().OfType<UIFieldAttribute>().Any() == true, $"{tab}: {config.Name}.{property} has no settings control");
            }
        }
    }
}
