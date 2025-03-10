﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Microsoft.EntityFrameworkCore.Scaffolding.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "Reviewed")]
    public class CommentCSharpEntityTypeGenerator : ICSharpEntityTypeGenerator
    {
        private readonly IAnnotationCodeGenerator annotationCodeGenerator;
        private readonly ICSharpHelper code;
        private readonly bool nullableReferences;
        private readonly bool noConstructor;

        private IndentedStringBuilder sb = null!;
        private bool useDataAnnotations;

        public CommentCSharpEntityTypeGenerator(
            [NotNull] IAnnotationCodeGenerator annotationCodeGenerator,
            [NotNull] ICSharpHelper cSharpHelper,
            bool nullableReferences,
            bool noConstructor)
        {
            Check.NotNull(cSharpHelper, nameof(cSharpHelper));

            this.annotationCodeGenerator = annotationCodeGenerator;
            code = cSharpHelper;
            this.nullableReferences = nullableReferences;
            this.noConstructor = noConstructor;
        }

#pragma warning disable CA1716 // Identifiers should not match keywords
        public virtual string WriteCode(IEntityType entityType, string @namespace, bool useDataAnnotations)
#pragma warning restore CA1716 // Identifiers should not match keywords
        {
            Check.NotNull(entityType, nameof(entityType));
            Check.NotNull(@namespace, nameof(@namespace));

            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            sb = new IndentedStringBuilder();
            this.useDataAnnotations = useDataAnnotations;

            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");

            if (this.useDataAnnotations)
            {
                sb.AppendLine("using System.ComponentModel.DataAnnotations;");
                sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
                sb.AppendLine("using Microsoft.EntityFrameworkCore;"); // For attributes coming out of Abstractions
            }

            foreach (var ns in entityType.GetProperties()
                .SelectMany(p => p.ClrType.GetNamespacesEx())
                .Where(ns => ns != "System" && ns != "System.Collections.Generic")
                .Distinct()
                .OrderBy(x => x, new NamespaceComparer()))
            {
                sb.AppendLine($"using {ns};");
            }

            sb.AppendLine();
            sb.AppendLine($"namespace {@namespace}");
            sb.AppendLine("{");

            using (sb.Indent())
            {
                GenerateClass(entityType);
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        protected virtual void GenerateClass([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            WriteComment(entityType.GetComment());

            if (useDataAnnotations)
            {
                GenerateEntityTypeDataAnnotations(entityType);
            }

            sb.AppendLine($"public partial class {entityType.Name}");

            sb.AppendLine("{");

            using (sb.Indent())
            {
                if (!noConstructor)
                {
                    GenerateConstructor(entityType);
                }

                GenerateProperties(entityType);
                GenerateNavigationProperties(entityType);
            }

            sb.AppendLine("}");
        }

        protected virtual void GenerateEntityTypeDataAnnotations([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            GenerateKeylessAttribute(entityType);
            GenerateTableAttribute(entityType);
            GenerateIndexAttributes(entityType);

            var annotations = annotationCodeGenerator
                .FilterIgnoredAnnotations(entityType.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            annotationCodeGenerator.RemoveAnnotationsHandledByConventions(entityType, annotations);

            foreach (var attribute in annotationCodeGenerator.GenerateDataAnnotationAttributes(entityType, annotations))
            {
                var attributeWriter = new AttributeWriter(attribute.Type.Name);
                foreach (var argument in attribute.Arguments)
                {
                    attributeWriter.AddParameter(code.UnknownLiteral(argument));
                }
            }
        }

        private void GenerateKeylessAttribute(IEntityType entityType)
        {
            if (entityType.FindPrimaryKey() == null)
            {
                sb.AppendLine(new AttributeWriter(nameof(KeylessAttribute)).ToString());
            }
        }

        private void GenerateTableAttribute(IEntityType entityType)
        {
            var tableName = entityType.GetTableName();
            var schema = entityType.GetSchema();
            var defaultSchema = entityType.Model.GetDefaultSchema();

            var schemaParameterNeeded = schema != null && schema != defaultSchema;
            var isView = entityType.GetViewName() != null;
            var tableAttributeNeeded = !isView && (schemaParameterNeeded || (tableName != null && tableName != entityType.GetDbSetName()));
            if (tableAttributeNeeded)
            {
                var tableAttribute = new AttributeWriter(nameof(TableAttribute));

                tableAttribute.AddParameter(code.Literal(tableName));

                if (schemaParameterNeeded)
                {
                    tableAttribute.AddParameter($"{nameof(TableAttribute.Schema)} = {code.Literal(schema)}");
                }

                sb.AppendLine(tableAttribute.ToString());
            }
        }

        private void GenerateIndexAttributes(IEntityType entityType)
        {
            // Do not generate IndexAttributes for indexes which
            // would be generated anyway by convention.
            foreach (var index in entityType.GetIndexes().Where(
                i => ((IConventionIndex)i).GetConfigurationSource() != ConfigurationSource.Convention))
            {
                // If there are annotations that cannot be represented using an IndexAttribute then use fluent API instead.
                var annotations = annotationCodeGenerator
                    .FilterIgnoredAnnotations(index.GetAnnotations())
                    .ToDictionary(a => a.Name, a => a);
                annotationCodeGenerator.RemoveAnnotationsHandledByConventions(index, annotations);

                if (annotations.Count == 0)
                {
                    var indexAttribute = new AttributeWriter(nameof(IndexAttribute));
                    foreach (var property in index.Properties)
                    {
                        indexAttribute.AddParameter($"nameof({property.Name})");
                    }

                    if (index.Name != null)
                    {
                        indexAttribute.AddParameter($"{nameof(IndexAttribute.Name)} = {code.Literal(index.Name)}");
                    }

                    if (index.IsUnique)
                    {
                        indexAttribute.AddParameter($"{nameof(IndexAttribute.IsUnique)} = {code.Literal(index.IsUnique)}");
                    }

                    sb.AppendLine(indexAttribute.ToString());
                }
            }
        }

        protected virtual void GenerateConstructor([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            var collectionNavigations = entityType.GetNavigations().Where(n => n.IsCollection).ToList();

            if (collectionNavigations.Count > 0)
            {
                sb.AppendLine($"public {entityType.Name}()");
                sb.AppendLine("{");

                using (sb.Indent())
                {
                    foreach (var navigation in collectionNavigations)
                    {
                        sb.AppendLine($"{navigation.Name} = new HashSet<{navigation.TargetEntityType.Name}>();");
                    }
                }

                sb.AppendLine("}");
                sb.AppendLine();
            }
        }

        protected virtual void GenerateProperties([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            if (entityType is null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            foreach (var property in entityType.GetProperties().OrderBy(p => p.GetColumnOrdinal()))
            {
                WriteComment(property.GetComment());

                if (useDataAnnotations)
                {
                    GeneratePropertyDataAnnotations(property);
                }

                string nullableAnnotation = string.Empty;
                string defaultAnnotation = string.Empty;

                if (nullableReferences && !property.ClrType.IsValueType)
                {
                    if (property.IsColumnNullable())
                    {
                        nullableAnnotation = "?";
                    }
                    else
                    {
                        defaultAnnotation = $" = default!;";
                    }
                }

                sb.AppendLine($"public {code.Reference(property.ClrType)}{nullableAnnotation} {property.Name} {{ get; set; }}{defaultAnnotation}");
            }
        }

#pragma warning disable CA1716 // Identifiers should not match keywords
        protected virtual void GeneratePropertyDataAnnotations([NotNull] IProperty property)
#pragma warning restore CA1716 // Identifiers should not match keywords
        {
            Check.NotNull(property, nameof(property));

            if (property is null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            GenerateKeyAttribute(property);
            GenerateRequiredAttribute(property);
            GenerateColumnAttribute(property);
            GenerateMaxLengthAttribute(property);

            var annotations = annotationCodeGenerator
                .FilterIgnoredAnnotations(property.GetAnnotations())
                .ToDictionary(a => a.Name, a => a);
            annotationCodeGenerator.RemoveAnnotationsHandledByConventions(property, annotations);

            foreach (var attribute in annotationCodeGenerator.GenerateDataAnnotationAttributes(property, annotations))
            {
                var attributeWriter = new AttributeWriter(attribute.Type.Name);
                foreach (var argument in attribute.Arguments)
                {
                    attributeWriter.AddParameter(code.UnknownLiteral(argument));
                }
            }
        }

        private void GenerateKeyAttribute(IProperty property)
        {
            var key = property.FindContainingPrimaryKey();
            if (key != null)
            {
                sb.AppendLine(new AttributeWriter(nameof(KeyAttribute)).ToString());
            }
        }

        private void GenerateColumnAttribute(IProperty property)
        {
            var columnName = property.GetColumnBaseName();
            var columnType = property.GetConfiguredColumnType();

            var delimitedColumnName = columnName != null && columnName != property.Name ? code.Literal(columnName) : null;
            var delimitedColumnType = columnType != null ? code.Literal(columnType) : null;

            if ((delimitedColumnName ?? delimitedColumnType) != null)
            {
                var columnAttribute = new AttributeWriter(nameof(ColumnAttribute));

                if (delimitedColumnName != null)
                {
                    columnAttribute.AddParameter(delimitedColumnName);
                }

#pragma warning disable CA1508 // Avoid dead conditional code
                if (delimitedColumnType != null)
                {
                    columnAttribute.AddParameter($"{nameof(ColumnAttribute.TypeName)} = {delimitedColumnType}");
                }
#pragma warning restore CA1508 // Avoid dead conditional code

                sb.AppendLine(columnAttribute.ToString());
            }
        }

        private void GenerateRequiredAttribute(IProperty property)
        {
            if (!property.IsNullable
                && property.ClrType.IsNullableType()
                && !property.IsPrimaryKey())
            {
                sb.AppendLine(new AttributeWriter(nameof(RequiredAttribute)).ToString());
            }
        }

        private void GenerateMaxLengthAttribute(IProperty property)
        {
            var maxLength = property.GetMaxLength();

            if (maxLength.HasValue)
            {
                var lengthAttribute = new AttributeWriter(
                    property.ClrType == typeof(string)
                        ? nameof(StringLengthAttribute)
                        : nameof(MaxLengthAttribute));

                lengthAttribute.AddParameter(code.Literal(maxLength.Value));

                sb.AppendLine(lengthAttribute.ToString());
            }
        }

        protected virtual void GenerateNavigationProperties([NotNull] IEntityType entityType)
        {
            Check.NotNull(entityType, nameof(entityType));

            var sortedNavigations = entityType.GetNavigations()
                .OrderBy(n => n.IsOnDependent ? 0 : 1)
                .ThenBy(n => n.IsCollection ? 1 : 0)
                .ToList();

            if (sortedNavigations.Any())
            {
                sb.AppendLine();

                foreach (var navigation in sortedNavigations)
                {
                    if (useDataAnnotations)
                    {
                        GenerateNavigationDataAnnotations(navigation);
                    }

                    var referencedTypeName = navigation.TargetEntityType.Name;
                    var navigationType = navigation.IsCollection ? $"ICollection<{referencedTypeName}>" : referencedTypeName;

                    string nullableAnnotation = string.Empty;
                    string defaultAnnotation = string.Empty;

                    if (nullableReferences && !navigation.IsCollection)
                    {
                        if (navigation.ForeignKey?.IsRequired == true)
                        {
                            defaultAnnotation = $" = default!;";
                        }
                        else
                        {
                            nullableAnnotation = "?";
                        }
                    }

                    sb.AppendLine($"public virtual {navigationType}{nullableAnnotation} {navigation.Name} {{ get; set; }}{defaultAnnotation}");
                }
            }
        }

        private void GenerateNavigationDataAnnotations(INavigation navigation)
        {
            GenerateForeignKeyAttribute(navigation);
            GenerateInversePropertyAttribute(navigation);
        }

        private void GenerateForeignKeyAttribute(INavigation navigation)
        {
            if (navigation.IsOnDependent && navigation.ForeignKey.PrincipalKey.IsPrimaryKey())
            {
                var foreignKeyAttribute = new AttributeWriter(nameof(ForeignKeyAttribute));

                if (navigation.ForeignKey.Properties.Count > 1)
                {
                    foreignKeyAttribute.AddParameter(
                        code.Literal(
                            string.Join(",", navigation.ForeignKey.Properties.Select(p => p.Name))));
                }
                else
                {
#pragma warning disable CA1826 // Do not use Enumerable methods on indexable collections
                    foreignKeyAttribute.AddParameter($"nameof({navigation.ForeignKey.Properties.First().Name})");
#pragma warning restore CA1826 // Do not use Enumerable methods on indexable collections
                }

                sb.AppendLine(foreignKeyAttribute.ToString());
            }
        }

        private void GenerateInversePropertyAttribute(INavigation navigation)
        {
            if (navigation.ForeignKey.PrincipalKey.IsPrimaryKey())
            {
                var inverseNavigation = navigation.Inverse;

                if (inverseNavigation != null)
                {
                    var inversePropertyAttribute = new AttributeWriter(nameof(InversePropertyAttribute));

                    inversePropertyAttribute.AddParameter(
                        !navigation.DeclaringEntityType.GetPropertiesAndNavigations().Any(
                            m => m.Name == inverseNavigation.DeclaringEntityType.Name)
                            ? $"nameof({inverseNavigation.DeclaringEntityType.Name}.{inverseNavigation.Name})"
                            : code.Literal(inverseNavigation.Name));

                    sb.AppendLine(inversePropertyAttribute.ToString());
                }
            }
        }

        private void WriteComment(string comment)
        {
            if (!string.IsNullOrWhiteSpace(comment))
            {
                sb.AppendLine("/// <summary>");

                foreach (var line in comment.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                {
                    sb.AppendLine($"/// {System.Security.SecurityElement.Escape(line)}");
                }

                sb.AppendLine("/// </summary>");
            }
        }

        private sealed class AttributeWriter
        {
            private readonly string attributeName;
            private readonly List<string> parameters = new List<string>();

            public AttributeWriter([NotNull] string attributeName)
            {
                Check.NotEmpty(attributeName, nameof(attributeName));

                this.attributeName = attributeName;
            }

            public void AddParameter([NotNull] string parameter)
            {
                Check.NotEmpty(parameter, nameof(parameter));

                parameters.Add(parameter);
            }

            public override string ToString()
                => "["
                    + (parameters.Count == 0
                        ? StripAttribute(attributeName)
                        : StripAttribute(attributeName) + "(" + string.Join(", ", parameters) + ")")
                    + "]";

            private static string StripAttribute([NotNull] string attributeName)
                => attributeName.EndsWith("Attribute", StringComparison.Ordinal)
                    ? attributeName[..^9]
                    : attributeName;
        }
    }
}
