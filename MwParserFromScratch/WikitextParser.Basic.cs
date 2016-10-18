﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MwParserFromScratch.Nodes;

namespace MwParserFromScratch
{
    /// <summary>
    /// A parser that parses Wikitext into AST.
    /// </summary>
    public partial class WikitextParser
    {
        /// <summary>
        /// WIKITEXT
        /// </summary>
        /// <remarks>The empty wikitext contains nothing. Thus the parsing should always be successful.</remarks>
        private Wikitext ParseWikitext()
        {
            ParseStart();
            var node = new Wikitext();
            LineNode lastLine = null;
            if (NeedsTerminate()) return ParseSuccessful(node);
            NEXT_LINE:
            var line = ParseLine(lastLine);
            if (line != EmptyLineNode)
            {
                lastLine = line;
                node.Lines.Add(line);
            }
            var extraPara = ParseLineEnd(lastLine);
            if (extraPara == null)
            {
                // Failed to read a \n , which means we've reached a terminator.
                // This is guaranteed in ParseLineEnd
                Debug.Assert(NeedsTerminate());
                return ParseSuccessful(node);
            }
            // Otherwise, check whether we meet a terminator before reading another line.
            if (extraPara != EmptyLineNode)
                node.Lines.Add(extraPara);
            if (NeedsTerminate()) return ParseSuccessful(node);
            goto NEXT_LINE;
        }

        /// <summary>
        /// Indicates the parsing is successful, but no node should be inserted to the list.
        /// </summary>
        private static readonly LineNode EmptyLineNode = new Paragraph(new PlainText("--EmptyLineNode--"));

        /// <summary>
        /// LINE, except PARAGRAPH. Because PARAGRAPH need to take look at the last line.
        /// </summary>
        private LineNode ParseLine(LineNode lastLine)
        {
            ParseStart(@"\n", false);
            LineNode node;
            // LIST_ITEM / HEADING automatically closes the last PARAGRAPH
            if ((node = ParseListItem()) != null) return ParseSuccessful(node);
            if ((node = ParseHeading()) != null) return ParseSuccessful(node);
            if ((node = ParseCompactParagraph(lastLine)) != null) return ParseSuccessful(node);
            return ParseFailed<LineNode>();
        }

        /// <summary>
        /// Parses a PARAGRPAH_CLOSE .
        /// </summary>
        /// <param name="lastNode">The lastest parsed paragrpah.</param>
        /// <returns>The extra paragraph, or <see cref="EmptyLineNode"/>. If parsing attempt failed, <c>null</c>.</returns>
        private LineNode ParseLineEnd(LineNode lastNode)
        {
            Debug.Assert(lastNode != null);
            var unclosedParagraph = lastNode as Paragraph;
            if (unclosedParagraph != null && !unclosedParagraph.Compact)
                unclosedParagraph = null;
            // 2 line breaks (\n\n) or \n Terminator closes the paragraph,
            // so do a look-ahead here. Note that a \n will be consumed in ParseWikitext .
            // Note that this look-ahead will also bypass the \n terminator defined in WIKITEXT

            // For the last non-empty line
            // TERM     Terminators
            // PC       Compact/unclosed paragraph
            // P        Closed paragraph
            // abc TERM     PC[|abc|]
            // abc\n TERM   P[|abc|]
            // abc\n\s*?\n  TERM PC[|abc|]PC[||]
            // Note that MediaWiki editor will automatically trim the trailing whitespaces,
            // leaving a \n after the content. This one \n will be removed when the page is transcluded.

            // Here we consume a \n without fallback.
            if (ConsumeToken(@"\n") == null)
                return null;
            ParseStart();
            // Whitespaces between 2 \n, assuming there's a second \n or TERM after trailingWs
            var trailingWs = ConsumeToken(@"[\f\r\t\v\x85\p{Z}]+");
            if (unclosedParagraph != null)
            {
                // We're going to consume another \n or TERM to close the paragraph.
                // Already consumed a \n, attempt to consume another \n
                if (ConsumeToken(@"\n") != null)
                {
                    // 2 Line breaks received.
                    // Note here TERM excludes \n
                    if (NeedsTerminate(Terminator.Get(@"\n")))
                    {
                        // This is a special case.
                        // abc\n trailingWs \n TERM --> PC[|abc|]PC[|trailingWs|]
                        var anotherparagraph = new Paragraph();
                        if (trailingWs != null) anotherparagraph.Append(trailingWs);
                        return ParseSuccessful(anotherparagraph);
                    }
                    // After the paragraph, more content incoming.
                    // abc\n trailingWs \n def
                    unclosedParagraph.Append("\n" + trailingWs);
                    return ParseSuccessful(EmptyLineNode, false);
                }
                // The attempt to consume the 2nd \n failed.
                // We're still after the whitespaces after the 1st \n .
                if (NeedsTerminate())
                {
                    // abc \n TERM   P[|abc|]
                    // Still need to close the paragraph.
                    unclosedParagraph.Append("\n" + trailingWs);
                    return ParseSuccessful(EmptyLineNode, false);
                }
            }
            else
            {
                // Last node cannot be a closed paragrap.
                // It can't because ONLY ParseLineEnd can close a paragraph.
                Debug.Assert(!(lastNode is Paragraph), "Last node cannot be a closed paragraph.");
                // Rather, last node is LINE node of other type (LIST_ITEM/HEADING).
                // Remember we've consumed a \n , and the spaces after it.
                if (NeedsTerminate(Terminator.Get(@"\n")))
                {
                    // abc \n TERM  -->  [|abc|] PC[||]
                    // Note here TERM excludes \n
                    var anotherparagraph = new Paragraph();
                    if (trailingWs != null) anotherparagraph.Append(trailingWs);
                    return ParseSuccessful(anotherparagraph);
                }
            }
            // abc \n def
            // That's not the end of a prargraph. Fallback to before the 1st \n .
            // Note here we have already consumed a \n .
            Fallback();
            return EmptyLineNode;
        }

        /// <summary>
        /// LIST_ITEM
        /// </summary>
        private ListItem ParseListItem()
        {
            ParseStart();
            var prefix = ConsumeToken("[*#:;]+|-{4,}| ");
            if (prefix == null) return ParseFailed<ListItem>();
            var node = new ListItem {Prefix = prefix};
            ParseRun(RunParsingMode.Run, node); // optional
            return ParseSuccessful(node);
        }

        /// <summary>
        /// HEADING
        /// </summary>
        private Heading ParseHeading()
        {
            // Look ahead to determine the maximum level, assuming the line is a valid heading.
            var prefix = LookAheadToken("={1,6}");
            if (prefix == null) return null;

            // Note that here we require all the headings terminate with \n or EOF, so this won't be recognized
            // {{{ARG|== Default Heading ==}}}\n
            // But this will work
            // {{{ARG|== Default Heading ==
            // }}}
            // Note that this should be recognized as heading
            //  == {{T|
            //  arg1}} ==

            // Test different levels of heading
            for (var level = prefix.Length; level > 0; level--)
            {
                var prefixExpr = "={" + level + "}";
                var suffixExpr = "(?m)={" + level + "}$";
                ParseStart(suffixExpr, false);
                ConsumeToken(prefixExpr);
                var node = new Heading();
                if (!ParseRun(RunParsingMode.Run, node))
                {
                    ParseFailed<Heading>();
                    continue;
                }
                if (ConsumeToken(suffixExpr) == null)
                {
                    ParseFailed<Heading>();
                    continue;
                }
                node.Level = level;
                return ParseSuccessful(node);
            }
            // Failed (E.g. ^=== Title )
            return null;
        }

        /// <summary>
        /// PARAGRAPH
        /// </summary>
        /// <remarks>The parsing operation will always succeed.</remarks>
        private LineNode ParseCompactParagraph(LineNode lastNode)
        {
            var mergeTo = lastNode as Paragraph;
            if (mergeTo != null && !mergeTo.Compact) mergeTo = null;
            // Create a new paragraph, or merge the new line to the last unclosed paragraph.
            ParseStart();
            mergeTo?.Append("\n");
            var node = mergeTo ?? new Paragraph();
            // Allows an empty paragraph/line.
            ParseRun(RunParsingMode.Run, node);
            if (node == mergeTo)
                return ParseSuccessful(EmptyLineNode, false);
            else
                return ParseSuccessful(node);
        }

        /// <summary>
        /// RUN
        /// </summary>
        /// <returns><c>true</c> if one or more nodes has been parsed.</returns>
        private bool ParseRun(RunParsingMode mode, InlineContainer container)
        {
            ParseStart();
            var parsedAny = false;
            while (!NeedsTerminate())
            {
                // Read more
                InlineNode inline = null;
                if ((inline = ParseExpandable()) != null) goto NEXT;
                switch (mode)
                {
                    case RunParsingMode.Run: // RUN
                        if ((inline = ParseInline()) != null) goto NEXT;
                        break;
                    case RunParsingMode.ExpandableText: // EXPANDABLE_TEXT
                        if ((inline = ParsePartialPlainText()) != null) goto NEXT;
                        break;
                    case RunParsingMode.ExpandableUrl: // EXPANDABLE_URL
                        if ((inline = ParseUrlText()) != null) goto NEXT;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
                }
                break;
                NEXT:
                parsedAny = true;
                // Remember that ParsePartialText stops whenever there's a susceptable termination of PLAIN_TEXT
                // So we need to marge the consequent PlainText objects.
                var newtext = inline as PlainText;
                if (newtext != null)
                {
                    var lastText = container.Inlines.LastNode as PlainText;
                    if (lastText != null)
                    {
                        lastText.Content += newtext.Content;
                        continue;
                    }
                }
                container.Inlines.Add(inline);
            }
            // Note that the content of RUN should not be empty.
            return parsedAny ? Accept() : Fallback();
        }

        private InlineNode ParseInline()
        {
            ParseStart();
            InlineNode node;
            if ((node = ParseTag()) != null) return ParseSuccessful(node);
            if ((node = ParseWikiLink()) != null) return ParseSuccessful(node);
            if ((node = ParseExternalLink()) != null) return ParseSuccessful(node);
            if ((node = ParseFormatSwitch()) != null) return ParseSuccessful(node);
            if ((node = ParsePartialPlainText()) != null) return ParseSuccessful(node);
            return ParseFailed<InlineNode>();
        }

        private InlineNode ParseExpandable()
        {
            ParseStart();
            InlineNode node;
            if ((node = ParseBraces()) != null) return ParseSuccessful(node);
            if ((node = ParseComment()) != null) return ParseSuccessful(node);
            return ParseFailed<InlineNode>();
        }

        private WikiLink ParseWikiLink()
        {
            // Note that wikilink cannot nest itself.
            ParseStart(@"\||\n|\[\[|\]\]", true);
            if (ConsumeToken(@"\[\[") == null) return ParseFailed<WikiLink>();
            var target = new Run();
            if (!ParseRun(RunParsingMode.ExpandableText, target)) return ParseFailed<WikiLink>();
            var node = new WikiLink {Target = target};
            if (ConsumeToken(@"\|") != null)
            {
                var text = new Run();
                // Text accepts pipe
                CurrentContext.Terminator = Terminator.Get(@"\n|\[\[|\]\]");
                // For [[target|]], Text == Empty Run
                // For [[target]], Text == null
                if (ParseRun(RunParsingMode.ExpandableText, text))
                    node.Text = text;
            }
            if (ConsumeToken(@"\]\]") == null) return ParseFailed<WikiLink>();
            return ParseSuccessful(node);
        }

        private ExternalLink ParseExternalLink()
        {
            ParseStart(@"[\s\]\|]", true);
            var brackets = ConsumeToken(@"\[") != null;
            // Parse target
            Run target;
            if (brackets)
            {
                target = new Run();
                // Aggressive
                ParseRun(RunParsingMode.ExpandableUrl, target);
            }
            else
            {
                // Conservative
                var url = ParseUrlText();
                target = url == null ? null : new Run(url);
            }
            if (target == null) return ParseFailed<ExternalLink>();
            var node = new ExternalLink {Target = target, Brackets = brackets};
            if (brackets)
            {
                // Parse text
                if (ConsumeToken(@"[ \t]") != null)
                {
                    CurrentContext.Terminator = Terminator.Get(@"[\]\n]");
                    var text = new Run();
                    // For [http://target  ], Text == " "
                    // For [http://target ], Text == Empty Run
                    // For [http://target], Text == null
                    if (ParseRun(RunParsingMode.Run, text))
                        node.Text = text;
                }
                if (ConsumeToken(@"\]") == null) return ParseFailed(node);
            }
            return ParseSuccessful(node);
        }

        private FormatSwitch ParseFormatSwitch()
        {
            // For 4 or 5+ quotes, discard quotes on the left.
            var token = ConsumeToken("('{5}|'''|'')(?!')");
            if (token == null) return null;
            switch (token.Length)
            {
                case 2:
                    return new FormatSwitch(false, true);
                case 3:
                    return new FormatSwitch(true, false);
                case 5:
                    return new FormatSwitch(true, true);
                default:
                    Debug.Assert(false);
                    return null;
            }
        }

        private void MovePositionTo(int newPosition)
        {
            if (newPosition <= position) throw new ArgumentOutOfRangeException(nameof(newPosition));
            // The position indicates the beginning of the next token.
            for (var i = position; i < newPosition; i++)
            {
                if (fulltext[i] == '\n')
                {
                    lineNumber++;
                    linePosition = 1;
                }
                else
                {
                    linePosition++;
                }
            }
            position = newPosition;
        }

        // Used to stop plain text parsing in case there's a starting of other elements.
        // Don't forget there's already a terminator \n in the LINE derivation
        private static readonly Regex SuspectablePlainTextEndMatcher =
            new Regex(@"\[|\{\{\{?
                        |<(\s*\w|!--)
                        |('{5}|'''|'')(?!')
                        |(((\bhttps?:|\bftp:|\birc:|\bgopher:|)\/\/)|\bnews:|\bmailto:)",
                RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

        /// <summary>
        /// PLAIN_TEXT
        /// </summary>
        /// <remarks>A PLAIN_TEXT contains at least 1 character.</remarks>
        private PlainText ParsePartialPlainText()
        {
            ParseStart();
            // Terminates now?
            if (NeedsTerminate()) return ParseFailed<PlainText>();
            // Or find the nearest terminator.
            // We'll at least consume 1 character.
            var terminatorPos = FindTerminator(1);
            // Remember, we'll at least consume 1 character.
            var susceptableEnd = SuspectablePlainTextEndMatcher.Match(fulltext, position + 1,
                terminatorPos - position - 1);
            var origPos = position;
            if (susceptableEnd.Success)
            {
                MovePositionTo(susceptableEnd.Index);
                return ParseSuccessful(new PlainText(fulltext.Substring(origPos, susceptableEnd.Index - origPos)));
            }
            else if (terminatorPos > 0)
            {
                MovePositionTo(terminatorPos);
                return ParseSuccessful(new PlainText(fulltext.Substring(origPos, terminatorPos - origPos)));
            }
            else
            {
                MovePositionTo(fulltext.Length);
                return ParseSuccessful(new PlainText(fulltext.Substring(origPos)));
            }
        }

        private static readonly Regex UrlMatcher =
            new Regex(
                @"(?i)(((\bhttps?:|\bftp:|\birc:|\bgopher:|)\/\/)|\bnews:|\bmailto:)([^\x00-\x20\s""\[\]\x7f\|\{\}<>]|<[^>]*>)+?(?=([!""().,:;‘-•]*\s|[\x00-\x20\s""\[\]\x7f|{}]|$))");

        private PlainText ParseUrlText()
        {
            // First, find an appearant terminator of URL
            var endPos = position;
            for (; endPos < fulltext.Length; endPos++)
                if (char.IsWhiteSpace(fulltext, endPos)) break;
            // From https://en.wikipedia.org/wiki/User:Cacycle/wikEd.js
            var match = UrlMatcher.Match(fulltext, position, endPos - position);
            // We only take the URL if it starts IMMEDIATELY.
            if (match.Success && match.Index == position)
            {
                MovePositionTo(position + match.Length);
                return new PlainText(match.Value);
            }
            return null;
        }

        private enum RunParsingMode
        {
            /// <summary>
            /// Single line text.
            /// </summary>
            Run = 0,
            /// <summary>
            /// Single line text with EXPANDABLE.
            /// </summary>
            ExpandableText = 1,
            /// <summary>
            /// Single line URL with EXPANDABLE.
            /// </summary>
            ExpandableUrl = 2,
        }
    }
}