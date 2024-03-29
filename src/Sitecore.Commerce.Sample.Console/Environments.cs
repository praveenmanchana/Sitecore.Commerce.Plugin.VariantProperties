﻿namespace Sitecore.Commerce.Sample.Console
{
    using System;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;

    using CommerceOps.Sitecore.Commerce.Core;
    using CommerceOps.Sitecore.Commerce.Plugin.Availability;
    using CommerceOps.Sitecore.Commerce.Plugin.Orders;
    using FluentAssertions;
    using Newtonsoft.Json;

    using Sitecore.Commerce.Plugin.Catalog;
    using Sitecore.Commerce.Sample.Contexts;
    using Sitecore.Commerce.ServiceProxy;

    public static class Environments
    {
        private static readonly CommerceOps.Sitecore.Commerce.Engine.Container OpsContainer
            = new DevOpAndre { Context = { Environment = "GlobalEnvironment" } }.Context.OpsContainer();

        public static void RunScenarios()
        {
            var watch = new Stopwatch();
            watch.Start();

            NewEnvironment();

            var habitat = Proxy.GetValue(OpsContainer.Environments.ByKey("Entity-CommerceEnvironment-HabitatAuthoring").Expand("Components"));
            habitat.Should().NotBeNull();

            Console.WriteLine("Begin Clone Environment");

            // Export an Environment to use as a template
            var originalEnvironment = ExportEnvironment("AdventureWorksShops");

            var environmentObject = JsonConvert.DeserializeObject(originalEnvironment);

            // Get a dynamic view of the JSON object
            var dynamicEnvironment = (dynamic)environmentObject;

            // Change the Id of the environment in order to import as a new Environment
            var newEnvironmentId = Guid.NewGuid().ToString("N");

            dynamicEnvironment.ArtifactStoreId = newEnvironmentId;
            dynamicEnvironment.Name = "ConsoleSample." + newEnvironmentId;

            var newSerializedEnvironment = JsonConvert.SerializeObject(environmentObject);

            // imports the environment into Sitecore Commerce
            var importedEnvironment = ImportEnvironment(newSerializedEnvironment);
            importedEnvironment.EnvironmentId.Should().Be($"Entity-CommerceEnvironment-ConsoleSample.{newEnvironmentId}");
            importedEnvironment.Name.Should().Be($"ConsoleSample.{newEnvironmentId}");

            // Adds a policy
            var policyResult = Proxy.DoOpsCommand(
                OpsContainer.AddPolicy(
                    importedEnvironment.EnvironmentId,
                    "Sitecore.Commerce.Plugin.Availability.GlobalAvailabilityPolicy, Sitecore.Commerce.Plugin.Availability",
                    new GlobalAvailabilityPolicy
                    {
                        AvailabilityExpires = 0
                    },
                    "GlobalEnvironment"));
            policyResult.Messages.Any(m => m.Code.Equals("error", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
            policyResult.Models.OfType<PolicyAddedModel>().Any().Should().BeTrue();

            // Initialize the Environment with default artifacts
            Bootstrapping.InitializeEnvironment(OpsContainer, $"ConsoleSample.{newEnvironmentId}");

            var shopperInNewEnvironmentContainer = new RegisteredCustomerDana
            {
                Context = { Environment = $"ConsoleSample.{newEnvironmentId}" }
            }.Context.ShopsContainer();

            // Get a SellableItem from the environment to assure that we have set it up correctly
            var result = Proxy.GetValue(shopperInNewEnvironmentContainer.SellableItems.ByKey("Adventure Works Catalog,AW055 01,")
                .Expand("Components($expand=ChildComponents($expand=ChildComponents($expand=ChildComponents)))"));
            result.Should().NotBeNull();
            result.Name.Should().Be("Unisex hiking pants");

            // Get the environment to validate change was made
            var updatedEnvironment = Proxy.GetValue(OpsContainer.Environments.ByKey(importedEnvironment.EnvironmentId));
            var globalAvailabilityPolicy = updatedEnvironment.Policies.OfType<GlobalAvailabilityPolicy>().FirstOrDefault();
            globalAvailabilityPolicy.Should().NotBeNull();
            globalAvailabilityPolicy.AvailabilityExpires.Should().Be(0);
            
            var orderListsPolicy = updatedEnvironment.Policies.OfType<KnownOrderListsPolicy>().FirstOrDefault();
            orderListsPolicy.Should().NotBeNull();
            orderListsPolicy.OrderDashboardLists = new ObservableCollection<KeyValueString>
            {
                new KeyValueString { Key = "listName", Value = "icon" }
            };
            policyResult = Proxy.DoOpsCommand(
                OpsContainer.AddPolicy(
                    importedEnvironment.EnvironmentId,
                    "Sitecore.Commerce.Plugin.Orders.KnownOrderListsPolicy, Sitecore.Commerce.Plugin.Orders",
                    orderListsPolicy,
                    "GlobalEnvironment"));
            policyResult.Messages.Any(m => m.Code.Equals("error", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
            policyResult.Models.OfType<PolicyAddedModel>().Any().Should().BeTrue();
            updatedEnvironment = Proxy.GetValue(OpsContainer.Environments.ByKey(importedEnvironment.EnvironmentId));
            orderListsPolicy = updatedEnvironment.Policies.OfType<KnownOrderListsPolicy>().FirstOrDefault();
            orderListsPolicy.Should().NotBeNull();
            orderListsPolicy.OrderDashboardLists.Should().OnlyContain(l => l.Key.Equals("listName"));

            watch.Stop();

            Console.WriteLine($"End Clone Environments: {watch.ElapsedMilliseconds} ms");
        }

        private static void NewEnvironment()
        {
            var environment = new Sitecore.Commerce.Core.CommerceEnvironment { Name = "ConsoleNewEnvironment" };
            var environmentJson = JsonConvert.SerializeObject(environment);

            ImportEnvironment(environmentJson);

            var getEnvironment = Proxy.GetValue(
                OpsContainer.Environments.ByKey("Entity-CommerceEnvironment-ConsoleNewEnvironment")
                    .Expand("Components"));
            getEnvironment.Should()
                .NotBeNull("Verify environment can be found (belongs in list) after it imported");

            var exportedEnvironmentJson = ExportEnvironment("ConsoleNewEnvironment");
            exportedEnvironmentJson.Should().NotBeNullOrEmpty("Verify environment exported");

            var exportedEnvironment =
                JsonConvert.DeserializeObject<Sitecore.Commerce.Core.CommerceEnvironment>(exportedEnvironmentJson);
            exportedEnvironment.Name.Should()
                .Be(
                    environment.Name,
                    "Verify exported environment can be deserialized and the environment name was preserved");
            exportedEnvironment.DisplayName = "New Display Name";

            ImportEnvironment(JsonConvert.SerializeObject(exportedEnvironment));

            var updatedEnvironmentJson = ExportEnvironment("ConsoleNewEnvironment");
            var updatedEnvironment =
                JsonConvert.DeserializeObject<Sitecore.Commerce.Core.CommerceEnvironment>(updatedEnvironmentJson);
            updatedEnvironment.DisplayName.Should().Be("New Display Name", "Verify updated environment");
        }
        
        private static ImportedEnvironmentModel ImportEnvironment(string environmentAsString)
        {
            var result = Proxy.DoOpsCommand(OpsContainer.ImportEnvironment(environmentAsString));

            result.Should().NotBeNull();
            result.Messages.Any(m => m.Code.Equals("error", StringComparison.OrdinalIgnoreCase)).Should().BeFalse();
            result.Models.OfType<ImportedEnvironmentModel>()
                .Any()
                .Should()
                .BeTrue();
            result.Models.OfType<ImportedEnvironmentModel>()
                .FirstOrDefault()
                ?.EnvironmentId.Should()
                .NotBeNullOrEmpty();

            return result.Models.OfType<ImportedEnvironmentModel>().First();
        }

        private static string ExportEnvironment(string environmentName)
        {
            var result = Proxy.GetValue(OpsContainer.ExportEnvironment(environmentName));
            result.Should().NotBeNull();
            return result;
        }
    }
}
