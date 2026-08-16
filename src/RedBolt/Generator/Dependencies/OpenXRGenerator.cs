// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.Generator.Dependencies;

using System;
using System.Collections.Generic;
using System.Linq;

using SharpCpp.Model.Ast;

internal sealed class OpenXRGenerator(string nativeLibraryName)
    : BindingTargetGenerator(
        "openxr",
        "RedBolt.Bindings.OpenXR",
        "RedBolt.Bindings.OpenXR",
        "OpenXR",
        nativeLibraryName,
        "redbolt_openxr",
        "-xc",
        "-std=c17",
        true)
{
    protected override string MarshallerClangLanguageArgument => "-xc++";

    protected override string MarshallerLanguageStandardArgument => "-std=c++20";

    protected override bool IncludeCallbackReleaseRegistration => true;

    protected override void ConfigureBeforeValidation(BindingCompilation compilation)
    {
        var aliasProjections = compilation.TypeAliases
            .Where(static alias => alias.CppQualifiedName.StartsWith("Xr", StringComparison.Ordinal))
            .Select(static alias => (Alias: alias, Projection: alias.TargetType.PrimitiveKind switch
            {
                BindingPrimitiveKind.NativeInt => new FixedWidthAliasProjection(BindingPrimitiveKind.Int64, "long"),
                BindingPrimitiveKind.NativeUInt => new FixedWidthAliasProjection(BindingPrimitiveKind.UInt64, "ulong"),
                _ => (FixedWidthAliasProjection?)null
            }))
            .Where(static pair => pair.Projection is not null)
            .ToArray();
        var fixedWidthAliases = new Dictionary<string, FixedWidthAliasProjection>(StringComparer.Ordinal)
        {
            ["int64_t"] = new(BindingPrimitiveKind.Int64, "long"),
            ["int64_t*"] = new(BindingPrimitiveKind.Int64, "long"),
            ["uint64_t"] = new(BindingPrimitiveKind.UInt64, "ulong"),
            ["uint64_t*"] = new(BindingPrimitiveKind.UInt64, "ulong")
        };
        foreach (var pair in aliasProjections)
        {
            var name = pair.Alias.CppQualifiedName;
            var projection = pair.Projection!.Value;
            fixedWidthAliases[name] = projection;
            fixedWidthAliases[$"{name}*"] = projection;
            fixedWidthAliases[$"const {name}*"] = projection;
        }

        FixedWidthAliasNormalizer.Normalize(compilation, fixedWidthAliases);

        // OpenXR record fields use count-and-pointer pairs for variable-sized primitive arrays.
        // A scalar projection loses the pointer needed while the native call is in progress, so
        // expose those fields as addresses and keep the original C type for the wrapper ABI.
        foreach (var field in compilation.Records
                     .SelectMany(static record => record.Members.OfType<FieldBinding>())
                     .Where(static field => field.IsPointer &&
                                            field.Type.Kind == BindingTypeKind.Primitive &&
                                            field.Type.PrimitiveKind != BindingPrimitiveKind.IntPtr))
        {
            field.Type = BindingType.IntPtr.WithCppTypeName(field.CppTypeName);
        }

        compilation.InvalidateCaches();
    }
}
