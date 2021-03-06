﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.EditAndContinue
{
    internal sealed class DocumentAnalysisResults
    {
        /// <summary>
        /// Spans of active statements in the document, or null if the document has syntax errors.
        /// </summary>
        public ImmutableArray<ActiveStatement> ActiveStatements { get; }

        /// <summary>
        /// Diagnostics for rude edits in the document, or empty if the document is unchanged or has syntax errors.
        /// If the compilation has semantic errors only syntactic rude edits are calculated.
        /// </summary>
        public ImmutableArray<RudeEditDiagnostic> RudeEditErrors { get; }

        /// <summary>
        /// Edits made in the document, or null if the document is unchanged, has syntax errors, has rude edits,
        /// or if the compilation has semantic errors.
        /// </summary>
        public ImmutableArray<SemanticEdit> SemanticEdits { get; }

        /// <summary>
        /// Exception regions -- spans of catch and finally handlers that surround the active statements.
        /// 
        /// Null if the document has syntax errors or rude edits, or if the compilation has semantic errors.
        /// </summary>
        /// <remarks>
        /// Null if there are any rude edit diagnostics.
        /// 
        /// Otherwise, each active statement in <see cref="ActiveStatements"/> has a corresponding slot in <see cref="ExceptionRegions"/>.
        ///
        /// Exception regions for each EH block/clause are marked as |...|.
        ///   try { ... AS ... } |catch { } finally { }|
        ///   try { } |catch { ... AS ... }| finally { }
        ///   try { } catch { } |finally { ... AS ... }|
        /// 
        /// Contains a minimal set of spans that cover the handlers.
        /// For example:
        ///   try { } |finally { try { ... AS ... } catch {  } }|
        ///   try { } |finally { try { } catch { ... AS ... } }|
        ///   try { try { } |finally { ... AS ... }| } |catch { } catch { } finally { }|
        /// </remarks>
        public ImmutableArray<ImmutableArray<LinePositionSpan>> ExceptionRegions { get; }

        /// <summary>
        /// Line edits in the document, or null if the document has syntax errors or rude edits, 
        /// or if the compilation has semantic errors.
        /// </summary>
        /// <remarks>
        /// Sorted by <see cref="LineChange.OldLine"/>
        /// </remarks>
        public ImmutableArray<LineChange> LineEdits { get; }

        /// <summary>
        /// The compilation has compilation errors (syntactic or semantic), 
        /// or null if the document doesn't have any modifications and
        /// presence of compilation errors was not determined.
        /// </summary>
        private readonly bool? _hasCompilationErrors;

        private DocumentAnalysisResults(ImmutableArray<RudeEditDiagnostic> rudeEdits)
        {
            Debug.Assert(!rudeEdits.IsDefault);
            _hasCompilationErrors = rudeEdits.Length == 0;
            RudeEditErrors = rudeEdits;
        }

        public DocumentAnalysisResults(
            ImmutableArray<ActiveStatement> activeStatements,
            ImmutableArray<RudeEditDiagnostic> rudeEdits,
            ImmutableArray<SemanticEdit> semanticEditsOpt,
            ImmutableArray<ImmutableArray<LinePositionSpan>> exceptionRegionsOpt,
            ImmutableArray<LineChange> lineEditsOpt,
            bool? hasSemanticErrors)
        {
            Debug.Assert(!rudeEdits.IsDefault);
            Debug.Assert(!activeStatements.IsDefault);
            Debug.Assert(activeStatements.All(a => a != null));

            if (hasSemanticErrors.HasValue)
            {

                if (hasSemanticErrors.Value || rudeEdits.Length > 0)
                {
                    Debug.Assert(semanticEditsOpt.IsDefault);
                    Debug.Assert(exceptionRegionsOpt.IsDefault);
                    Debug.Assert(lineEditsOpt.IsDefault);
                }
                else
                {
                    Debug.Assert(!semanticEditsOpt.IsDefault);
                    Debug.Assert(!exceptionRegionsOpt.IsDefault);
                    Debug.Assert(!lineEditsOpt.IsDefault);

                    Debug.Assert(exceptionRegionsOpt.Length == activeStatements.Length);
                }
            }
            else
            {
                Debug.Assert(semanticEditsOpt.IsEmpty);
                Debug.Assert(lineEditsOpt.IsEmpty);

                Debug.Assert(exceptionRegionsOpt.IsDefault || exceptionRegionsOpt.Length == activeStatements.Length);
            }

            RudeEditErrors = rudeEdits;
            SemanticEdits = semanticEditsOpt;
            ActiveStatements = activeStatements;
            ExceptionRegions = exceptionRegionsOpt;
            LineEdits = lineEditsOpt;
            _hasCompilationErrors = hasSemanticErrors;
        }

        public bool HasChanges => _hasCompilationErrors.HasValue;

        public bool HasChangesAndErrors
        {
            get
            {
                return HasChanges && (_hasCompilationErrors.Value || !RudeEditErrors.IsEmpty);
            }
        }

        public bool HasChangesAndCompilationErrors
        {
            get
            {
                return _hasCompilationErrors == true;
            }
        }

        public bool HasSignificantValidChanges
        {
            get
            {
                return HasChanges && (!SemanticEdits.IsDefaultOrEmpty || !LineEdits.IsDefaultOrEmpty);
            }
        }

        public static DocumentAnalysisResults SyntaxErrors(ImmutableArray<RudeEditDiagnostic> rudeEdits)
            => new DocumentAnalysisResults(rudeEdits);

        public static DocumentAnalysisResults Unchanged(
            ImmutableArray<ActiveStatement> activeStatements,
            ImmutableArray<ImmutableArray<LinePositionSpan>> exceptionRegionsOpt)
        {
            return new DocumentAnalysisResults(
                activeStatements,
                ImmutableArray<RudeEditDiagnostic>.Empty,
                ImmutableArray<SemanticEdit>.Empty,
                exceptionRegionsOpt,
                ImmutableArray<LineChange>.Empty,
                hasSemanticErrors: null);
        }

        public static DocumentAnalysisResults Errors(
            ImmutableArray<ActiveStatement> activeStatements,
            ImmutableArray<RudeEditDiagnostic> rudeEdits,
            bool hasSemanticErrors = false)
        {
            return new DocumentAnalysisResults(
                activeStatements,
                rudeEdits,
                default,
                default,
                default,
                hasSemanticErrors);
        }

        internal static readonly TraceLog Log = new TraceLog(256, "EnC");
    }
}
