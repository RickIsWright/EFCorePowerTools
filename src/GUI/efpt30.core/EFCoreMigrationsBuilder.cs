﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
#if CORE60
using Microsoft.EntityFrameworkCore.Metadata;
#endif
#if CORE50
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
#endif
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Modelling
{
    public static class EfCoreMigrationsBuilder
    {
#pragma warning disable CA1002 // Do not expose generic lists
        public static List<Tuple<string, string>> GenerateMigrationStatusList(string outputPath, string startupOutputPath)
        {
            return GetMigrationStatus(outputPath, startupOutputPath ?? outputPath);
        }

        public static List<Tuple<string, string>> Migrate(string outputPath, string startupOutputPath, string contextName)
        {
            return BuildMigrationResult(outputPath, startupOutputPath ?? outputPath, null, contextName, false, false, null, null);
        }

        public static List<Tuple<string, string>> AddMigration(string outputPath, string startupOutputPath, string projectPath, string contextName, string migrationIdentifier, string nameSpace)
        {
            return BuildMigrationResult(outputPath, startupOutputPath ?? outputPath, projectPath, contextName, true, false, migrationIdentifier, nameSpace);
        }

        public static List<Tuple<string, string>> ScriptMigration(string outputPath, string startupOutputPath, string contextName)
        {
            return BuildMigrationResult(outputPath, startupOutputPath ?? outputPath, null, contextName, false, true, null, null);
        }
#pragma warning restore CA1002 // Do not expose generic lists

        private static List<Tuple<string, string>> BuildMigrationResult(string outputPath, string startupOutputPath, string projectPath, string contextName, bool addMigration, bool scriptMigration, string migrationIdentifier, string nameSpace)
        {
            var result = new List<Tuple<string, string>>();
            var operations = GetOperations(outputPath, startupOutputPath);
            var types = GetDbContextTypes(operations);

            foreach (var type in types)
            {
                if (type.Name == contextName)
                {
                    var dbContext = operations.CreateContext(type.Name);
                    if (scriptMigration)
                    {
                        result.Add(new Tuple<string, string>(type.Name, ScriptMigration(dbContext, outputPath, startupOutputPath)));
                    }
                    else
                    {
                        result.Add(addMigration
                            ? new Tuple<string, string>(type.Name + "Add", AddMigration(dbContext, outputPath, startupOutputPath, projectPath, migrationIdentifier, nameSpace))
                            : new Tuple<string, string>(type.Name + "Apply", ApplyMigrations(dbContext)));
                    }

                    break;
                }
            }

            return result;
        }

        private static List<Tuple<string, string>> GetMigrationStatus(string outputPath, string startupOutputPath)
        {
            var result = new List<Tuple<string, string>>();
            var operations = GetOperations(outputPath, startupOutputPath);
            var types = GetDbContextTypes(operations);

            foreach (var type in types)
            {
                var dbContext = operations.CreateContext(type.Name);
                result.Add(new Tuple<string, string>(type.Name, GetMigrationStatus(dbContext)));
            }

            return result;
        }

        private static string GetMigrationStatus(DbContext context)
        {
            var relationalDatabaseCreator = context.GetService<IDatabaseCreator>() as IRelationalDatabaseCreator;
            if (relationalDatabaseCreator == null)
            {
                throw new InvalidOperationException("Not a relational database, migrations are not supported");
            }

            var databaseExists = relationalDatabaseCreator.Exists();

            var migrationsAssembly = context.GetService<IMigrationsAssembly>();
#if CORE50
            var modelDiffer = context.GetService<IMigrationsModelDiffer>();

            var hasDifferences = false;
            var dependencies = context.GetService<ProviderConventionSetBuilderDependencies>();
            var relationalDependencies = context.GetService<RelationalConventionSetBuilderDependencies>();

            if (migrationsAssembly.ModelSnapshot != null)
            {
                var typeMappingConvention = new TypeMappingConvention(dependencies);
                typeMappingConvention.ProcessModelFinalizing(((IConventionModel)migrationsAssembly.ModelSnapshot.Model).Builder, null);

                var relationalModelConvention = new RelationalModelConvention(dependencies, relationalDependencies);
                var sourceModel = relationalModelConvention.ProcessModelFinalized(migrationsAssembly.ModelSnapshot.Model);

                hasDifferences = modelDiffer.HasDifferences(
                    ((IMutableModel)sourceModel).FinalizeModel().GetRelationalModel(),
                    context.Model.GetRelationalModel());
            }

            var pendingModelChanges = !databaseExists || hasDifferences;
#elif CORE60
            var hasDifferences = false;
            if (migrationsAssembly?.ModelSnapshot != null)
            {
                var snapshotModel = migrationsAssembly.ModelSnapshot.Model;

                if (snapshotModel is IMutableModel mutableModel)
                {
                    snapshotModel = mutableModel.FinalizeModel();
                }

                if (snapshotModel != null)
                {
                    snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel);
                    hasDifferences = context.GetService<IMigrationsModelDiffer>().HasDifferences(
                        snapshotModel.GetRelationalModel(),
                        context.GetService<IDesignTimeModel>().Model.GetRelationalModel());
                }
            }

            var pendingModelChanges = !databaseExists || hasDifferences;
#else
            var modelDiffer = context.GetService<IMigrationsModelDiffer>();

            var pendingModelChanges
                = (!databaseExists || migrationsAssembly.ModelSnapshot != null)
                    && modelDiffer.HasDifferences(migrationsAssembly.ModelSnapshot?.Model, context.Model);
#endif
            if (pendingModelChanges)
            {
                return "Changes";
            }

            var migrations = context.Database.GetMigrations().ToArray();

            if (!migrations.Any())
            {
                return "NoMigrations";
            }

            var pendingMigrations = context.Database.GetPendingMigrations().ToArray();

            if (pendingMigrations.Any())
            {
                return "Pending";
            }

            return "InSync";
        }

        private static string ScriptMigration(DbContext context, string outputPath, string startupOutputPath)
        {
            var servicesBuilder = GetDesignTimeServicesBuilder(outputPath, startupOutputPath);

            var services = servicesBuilder.Build(context);

            EnsureServices(services);

            var migrator = services.GetRequiredService<IMigrator>();
#if CORE50 || CORE60
            return migrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.Idempotent);
#else
            return migrator.GenerateScript(null, null, idempotent: true);
#endif
        }

        private static string ApplyMigrations(DbContext context)
        {
            context.Database.Migrate();

            return "Done";
        }

        private static string AddMigration(DbContext context, string outputPath, string startupOutputPath, string projectPath, string name, string nameSpace)
        {
            var result = AddMigration(name, outputPath, startupOutputPath, projectPath, nameSpace, context);
            string status = result.MetadataFile + Environment.NewLine;
            status += result.MigrationFile + Environment.NewLine;
            status += result.SnapshotFile + Environment.NewLine;
            return status;
        }

        private static MigrationFiles AddMigration(
            string name,
            string outputPath,
            string startupOutputPath,
            string projectPath,
            string nameSpace,
            DbContext context)
        {
            var servicesBuilder = GetDesignTimeServicesBuilder(outputPath, startupOutputPath);

            var services = servicesBuilder.Build(context);

            EnsureServices(services);

            var assembly = Load(outputPath);
            if (assembly == null)
            {
                throw new ArgumentException("Unable to load project assembly");
            }

            EnsureMigrationsAssembly(services, assembly);

            var scaffolder = services.GetRequiredService<IMigrationsScaffolder>();
            var migration = scaffolder.ScaffoldMigration(name, nameSpace);

            return scaffolder.Save(projectPath, migration, null);
        }

        private static List<Type> GetDbContextTypes(DbContextOperations operations)
        {
            var types = operations.GetContextTypes().ToList();
            if (types.Count == 0)
            {
                throw new ArgumentException("No EF Core DbContext types found in the project");
            }

            return types;
        }

        private static DbContextOperations GetOperations(string outputPath, string startupOutputPath)
        {
            var assembly = Load(outputPath);
            if (assembly == null)
            {
                throw new ArgumentException("Unable to load project assembly");
            }

            Assembly startupAssembly = null;

            if (startupOutputPath != outputPath)
            {
                startupAssembly = Load(startupOutputPath);
            }

            var reporter = new OperationReporter(
                new OperationReportHandler());
#if CORE60
            return new DbContextOperations(reporter, assembly, startupAssembly ?? assembly, outputPath, null, null, false, Array.Empty<string>());
#else
            return new DbContextOperations(reporter, assembly, startupAssembly ?? assembly, Array.Empty<string>());
#endif
        }

        private static DesignTimeServicesBuilder GetDesignTimeServicesBuilder(string outputPath, string startupOutputPath)
        {
            var assembly = Load(outputPath);
            if (assembly == null)
            {
                throw new ArgumentException("Unable to load project assembly");
            }

            Assembly startupAssembly = null;

            if (startupOutputPath != outputPath)
            {
                startupAssembly = Load(startupOutputPath);
            }

            var reporter = new OperationReporter(
                new OperationReportHandler());

            return new DesignTimeServicesBuilder(assembly, startupAssembly ?? assembly, reporter, Array.Empty<string>());
        }

        private static Assembly Load(string assemblyPath)
        {
            return File.Exists(assemblyPath) ? Assembly.LoadFile(assemblyPath) : null;
        }

        private static void EnsureServices(IServiceProvider services)
        {
            var migrator = services.GetService<IMigrator>();
            if (migrator == null)
            {
                var databaseProvider = services.GetService<IDatabaseProvider>();
                throw new OperationException(DesignStrings.NonRelationalProvider(databaseProvider?.Name ?? "Unknown"));
            }
        }

        private static void EnsureMigrationsAssembly(IServiceProvider services, Assembly assembly)
        {
            var assemblyName = assembly.GetName();
            var options = services.GetRequiredService<IDbContextOptions>();
            var contextType = services.GetRequiredService<ICurrentDbContext>().Context.GetType();
            var migrationsAssemblyName = RelationalOptionsExtension.Extract(options).MigrationsAssembly
                                         ?? contextType.GetTypeInfo().Assembly.GetName().Name;
            if (assemblyName.Name != migrationsAssemblyName
                && assemblyName.FullName != migrationsAssemblyName)
            {
                throw new OperationException(
                    DesignStrings.MigrationsAssemblyMismatch(assemblyName.Name, migrationsAssemblyName));
            }
        }
    }
}
