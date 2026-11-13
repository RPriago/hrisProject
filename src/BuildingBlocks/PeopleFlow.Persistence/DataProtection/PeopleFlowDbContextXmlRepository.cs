using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PeopleFlow.Persistence.Context;
using PeopleFlow.Persistence.Entities;

namespace PeopleFlow.Persistence.DataProtection;

/// <summary>
/// Persists ASP.NET Core Data Protection XML keys into <see cref="PeopleFlowDbContext"/>.
/// </summary>
public sealed class PeopleFlowDbContextXmlRepository : IXmlRepository
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PeopleFlowDbContextXmlRepository(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PeopleFlowDbContext>();

        var rows = dbContext.DataProtectionKeys
            .AsNoTracking()
            .Select(k => new { k.CreatedAtUtc, k.Id, k.Xml })
            .ToList()
            .OrderBy(k => k.CreatedAtUtc)
            .ThenBy(k => k.Id)
            .Select(k => k.Xml);

        var elements = new List<XElement>();
        foreach (var xml in rows)
        {
            if (!string.IsNullOrWhiteSpace(xml))
            {
                elements.Add(XElement.Parse(xml));
            }
        }

        return elements.AsReadOnly();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PeopleFlowDbContext>();

        var record = new DataProtectionKeyRecord
        {
            Id = Guid.CreateVersion7(),
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName)
                ? null
                : friendlyName.Length > 256 ? friendlyName[..256] : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting)
        };

        dbContext.DataProtectionKeys.Add(record);
        dbContext.SaveChanges();
    }
}

public static class DataProtectionBuilderPersistenceExtensions
{
    public static IDataProtectionBuilder PersistKeysToPeopleFlowDbContext(this IDataProtectionBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new ConfigureOptions<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new PeopleFlowDbContextXmlRepository(scopeFactory);
            });
        });

        return builder;
    }
}
