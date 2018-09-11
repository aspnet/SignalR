// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("StyleCop.CSharp.SpecialRules", "SA0001:XML comment analysis is disabled due to project configuration", Justification = "Documentation is enforced with manual review.")]

[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1309:Field names should not begin with underscore", Justification = "Naming is enforced with manual review.")]
[assembly: SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names should not contain underscore", Justification = "Naming is enforced with manual review.")]

[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1128:Put constructor initializers on their own line", Justification = "ASP.NET Core Style does not prescribe a specific layout of constructors.")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:Prefix local calls with this", Justification = "ASP.NET Core Style does not prescribe a specific layout of constructors.")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1127:Generic type constraints should be on their own line", Justification = "ASP.NET Core Style does not prescribe a specific layout of generic constraints.")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1129:Do not use default value type constructor", Justification = "Usage of value type constructor is enforced with manual review.")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1117:Parameters should be on same line or separate lines", Justification = "ASP.NET Core Style does not prescribe a specific layout of parameters.")]
[assembly: SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1116:Split parameters should start on line after declaration", Justification = "ASP.NET Core Style does not prescribe a specific layout of parameters.")]

[assembly: SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1516:Elements should be separated by blank line", Justification = "ASP.NET Core Style does not prescribe a specific spacing between members.")]
[assembly: SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1513:Closing brace should be followed by blank line", Justification = "ASP.NET Core Style does not prescribe a specific spacing between members.")]
[assembly: SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1502:Element should not be on a single line", Justification = "Member layout is enforced with manual review.")]
[assembly: SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1512:Single-line comments should not be followed by blank line", Justification = "ASP.NET Core Style does not prescribe a specific comment layout.")]
[assembly: SuppressMessage("StyleCop.CSharp.LayoutRules", "SA1515:Single-line comment should be preceded by blank line", Justification = "ASP.NET Core Style does not prescribe a specific comment layout.")]

[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:Static elements should appear before instance elements", Justification = "ASP.NET Core Style does not prescribe a specific ordering of members.")]
[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1214:Readonly fields should appear before non-readonly fields", Justification = "ASP.NET Core Style does not prescribe a specific ordering of members.")]
[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:Elements should appear in the correct order", Justification = "ASP.NET Core Style does not prescribe a specific ordering of members.")]
[assembly: SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "ASP.NET Core Style does not prescribe a specific ordering of members.")]

[assembly: SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1005:Single line comments should begin with single space", Justification = "ASP.NET Core Style does not prescribe comment spacing.")]

[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "We manually review documentation needs.")]
[assembly: SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1601:Partial elements should be documented", Justification = "We manually review documentation needs.")]

[assembly: SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:File may only contain a single type", Justification = "There are many exceptions to this rule in our style, so it's better enforced manually.")]
