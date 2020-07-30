﻿using System.Collections.Generic;
using System.Threading.Tasks;
using EfLocalDb;
using GraphQL;
using GraphQL.EntityFramework;
using GraphQL.Utilities;
using Microsoft.Extensions.DependencyInjection;
using VerifyXunit;
using Xunit;

[UsesVerify]
public class MultiContextTests
{
    [Fact]
    public async Task Run()
    {
        GraphTypeTypeRegistry.Register<Entity1, Entity1Graph>();
        GraphTypeTypeRegistry.Register<Entity2, Entity2Graph>();

        var sqlInstance1 = new SqlInstance<DbContext1>(
            constructInstance: builder => new DbContext1(builder.Options));

        var sqlInstance2 = new SqlInstance<DbContext2>(
            constructInstance: builder => new DbContext2(builder.Options));

        var query = @"
{
  entity1
  {
    property
  },
  entity2
  {
    property
  }
}";

        var entity1 = new Entity1
        {
            Property = "the entity1"
        };
        var entity2 = new Entity2
        {
            Property = "the entity2"
        };

        var services = new ServiceCollection();

        services.AddSingleton<MultiContextQuery>();
        services.AddSingleton<Entity1Graph>();
        services.AddSingleton<Entity2Graph>();

        await using (var database1 = await sqlInstance1.Build())
        await using (var database2 = await sqlInstance2.Build())
        {
            await database1.AddDataUntracked(entity1);
            await database2.AddDataUntracked(entity2);

            var dbContext1 = database1.NewDbContext();
            var dbContext2 = database2.NewDbContext();
            services.AddSingleton(dbContext1);
            services.AddSingleton(dbContext2);

            #region RegisterMultipleInContainer

            EfGraphQLConventions.RegisterInContainer(
                services,
                userContext => ((UserContext) userContext).DbContext1);
            EfGraphQLConventions.RegisterInContainer(
                services,
                userContext => ((UserContext) userContext).DbContext2);

            #endregion

            await using var provider = services.BuildServiceProvider();
            using var schema = new MultiContextSchema(provider);
            var documentExecuter = new EfDocumentExecuter();

            #region MultiExecutionOptions

            var executionOptions = new ExecutionOptions
            {
                Schema = schema,
                Query = query,
                UserContext = new UserContext(dbContext1, dbContext2)
            };

            #endregion

            var result = await documentExecuter.ExecuteWithErrorCheck(executionOptions);
            await Verifier.Verify(result.Data);
        }
    }
}

#region MultiUserContext
public class UserContext: Dictionary<string, object>
{
    public UserContext(DbContext1 context1, DbContext2 context2)
    {
        DbContext1 = context1;
        DbContext2 = context2;
    }

    public readonly DbContext1 DbContext1;
    public readonly DbContext2 DbContext2;
}
#endregion