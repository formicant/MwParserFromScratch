﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MwParserFromScratch.Nodes
{
    public abstract class InlineNode : Node
    {
        
    }

    public class PlainText : InlineNode
    {
        public PlainText() : this(null)
        {
        }

        public PlainText(string content)
        {
            Content = content;
        }

        public string Content { get; set; }

        protected override Node CloneCore()
        {
            return new PlainText {Content = Content};
        }

        public override string ToString()
        {
            return Content;
        }
    }

    public class WikiLink : InlineNode
    {
        private Run _Target;
        private Run _Text;

        public Run Target
        {
            get { return _Target; }
            set { Attach(ref _Target, value); }
        }

        public Run Text
        {
            get { return _Text; }
            set { Attach(ref _Text, value); }
        }

        protected override Node CloneCore()
        {
            return new WikiLink {Target = Target, Text = Text};
        }

        public override string ToString() => Text == null ? $"[[{Target}]]" : $"[[{Target}|{Text}]]";
    }

    public class ExternalLink : InlineNode
    {
        private Run _Target;
        private Run _Text;

        public Run Target
        {
            get { return _Target; }
            set { Attach(ref _Target, value); }
        }

        public Run Text
        {
            get { return _Text; }
            set { Attach(ref _Text, value); }
        }

        /// <summary>
        /// Whether the link is contained in square brackets.
        /// </summary>
        public bool Brackets { get; set; }

        protected override Node CloneCore()
        {
            return new ExternalLink { Target = Target, Text = Text };
        }

        public override string ToString()
        {
            var s = Target.ToString();
            if (Text != null) s += " " + Text;
            if (Brackets) s = "[" + s + "]";
            return s;
        }
    }

    /// <summary>
    /// Represents wikitext with bold / italics.
    /// </summary>
    public class FormatSwitch : InlineNode
    {
        public FormatSwitch() : this(false, false)
        {
        }

        public FormatSwitch(bool switchBold, bool switchItalics)
        {
            SwitchBold = switchBold;
            SwitchItalics = switchItalics;
        }

        /// <summary>
        /// Whether to switch font-bold of the incoming content.
        /// </summary>
        public bool SwitchBold { get; set; }

        /// <summary>
        /// Whether to switch font-italics of the incoming content.
        /// </summary>
        public bool SwitchItalics { get; set; }

        protected override Node CloneCore()
        {
            var n = new FormatSwitch {SwitchBold = SwitchBold, SwitchItalics = SwitchItalics};
            return n;
        }

        public override string ToString()
        {
            if (SwitchBold && SwitchItalics)
                return "'''''";
            if (SwitchBold)
                return "'''";
            if (SwitchItalics)
                return "''";
            return "";
        }
    }

    public class Template : InlineNode
    {
        private Run _Name;

        public Template() : this(null)
        {
        }

        public Template(Run name)
        {
            Name = name;
            Arguments = new NodeCollection<TemplateArgument>(this);
        }

        public Run Name
        {
            get { return _Name; }
            set { Attach(ref _Name, value); }
        }

        public NodeCollection<TemplateArgument> Arguments { get; }

        protected override Node CloneCore()
        {
            var n = new Template {Name = Name};
            n.Arguments.Add(Arguments);
            return n;
        }

        public override string ToString()
        {
            if (Arguments.IsEmpty) return "{{" + Name + "}}";
            var sb = new StringBuilder("{{");
            sb.Append(Name);
            foreach (var arg in Arguments)
            {
                sb.Append('|');
                sb.Append(arg);
            }
            sb.Append("}}");
            return sb.ToString();
        }
    }

    public class TemplateArgument : Node
    {
        private Wikitext _Name;
        private Wikitext _Value;

        public TemplateArgument() : this(null, null)
        {
        }

        public TemplateArgument(Wikitext name, Wikitext value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        /// Name of the argument.
        /// </summary>
        /// <value>Name of the argument, or <c>null</c> if the argument is anonymous.</value>
        public Wikitext Name
        {
            get { return _Name; }
            set { Attach(ref _Name, value); }
        }

        public Wikitext Value
        {
            get { return _Value; }
            set { Attach(ref _Value, value); }
        }

        protected override Node CloneCore()
        {
            var n = new TemplateArgument {Name = Name, Value = Value};
            return n;
        }

        public override string ToString()
        {
            if (Name == null) return Value.ToString();
            return Name + "=" + Value;
        }
    }

    /// <summary>
    /// {{{name|defalut}}}
    /// </summary>
    public class ArgumentReference : InlineNode
    {
        private Wikitext _Name;
        private Wikitext _DefaultValue;

        public ArgumentReference() : this(null, null)
        {
        }

        public ArgumentReference(Wikitext name) : this(name, null)
        {
        }

        public ArgumentReference(Wikitext name, Wikitext defaultValue)
        {
            Name = name;
            DefaultValue = defaultValue;
        }

        /// <summary>
        /// Name of the argument.
        /// </summary>
        /// <value>Name of the argument.</value>
        public Wikitext Name
        {
            get { return _Name; }
            set { Attach(ref _Name, value); }
        }

        public Wikitext DefaultValue
        {
            get { return _DefaultValue; }
            set { Attach(ref _DefaultValue, value); }
        }

        protected override Node CloneCore()
        {
            var n = new ArgumentReference { Name = Name, DefaultValue = DefaultValue };
            return n;
        }

        public override string ToString()
        {
            var s = "{{{" + Name;
            if (DefaultValue != null) s += "|" + DefaultValue;
            return s + "}}}";
        }
    }

    /// <summary>
    /// &lt;tag attr1=value1&gt;content&lt;/tag&gt;
    /// </summary>
    public abstract class TagNode : Node
    {
        private string _TrailingWhitespace;

        public TagNode()
        {
            Attributes = new NodeCollection<TagAttribute>(this);
        }

        /// <summary>
        /// Name of the tag.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Name of the closing tag. It may have a different letter-case from <see cref="Name"/>.
        /// </summary>
        /// <value>The name of closing tag. OR <c>null</c> if it shares exactly the same content as <see cref="Name"/>.</value>
        public string ClosingTagName { get; set; }

        /// <summary>
        /// Whether the tag is self closed. E.g. &lt;references /&gt;.
        /// </summary>
        public abstract bool IsSelfClosing { get; set; }

        /// <summary>
        /// The trailing whitespace for the opening tag.
        /// </summary>
        /// <exception cref="ArgumentException">The string contains non-white-space characters.</exception>
        public string TrailingWhitespace
        {
            get { return _TrailingWhitespace; }
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Only null or white spaces are accepted.", nameof(value));
                _TrailingWhitespace = value;
            }
        }

        /// <summary>
        /// The trailing whitespace for the closing tag.
        /// </summary>
        /// <exception cref="ArgumentException">The string contains non-white-space characters.</exception>
        public string ClosingTagTrailingWhitespace
        {
            get { return _TrailingWhitespace; }
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Only null or white spaces are accepted.", nameof(value));
                _TrailingWhitespace = value;
            }
        }

        public NodeCollection<TagAttribute> Attributes { get; }

        protected abstract string GetContentString();

        public override string ToString()
        {
            var sb = new StringBuilder("<");
            sb.Append(Name);
            sb.Append(string.Join(null, Attributes));
            sb.Append(TrailingWhitespace);
            if (IsSelfClosing)
            {
                sb.Append("/>");
                return sb.ToString();
            }
            sb.Append('>');
            sb.Append(GetContentString());
            sb.Append('<');
            sb.Append(ClosingTagName ?? Name);
            sb.Append(ClosingTagTrailingWhitespace);
            sb.Append('>');
            return sb.ToString();
        }
    }

    public class ParserTag : TagNode
    {
        /// <summary>
        /// Raw content of the tag.
        /// </summary>
        /// <value>Content of the tag, as string. If the tag is self-closing, the value is <c>null</c>.</value>
        public string Content { get; set; }

        protected override Node CloneCore()
        {
            var n = new ParserTag
            {
                Name = Name,
                ClosingTagName = ClosingTagName,
                Content = Content,
                TrailingWhitespace = TrailingWhitespace,
                ClosingTagTrailingWhitespace = ClosingTagTrailingWhitespace,
            };
            n.Attributes.Add(Attributes);
            return n;
        }

        /// <summary>
        /// Whether the tag is self closed. E.g. &lt;references /&gt;.
        /// </summary>
        public override bool IsSelfClosing
        {
            get { return Content == null; }
            set
            {
                if (value)
                {
                    if (!string.IsNullOrEmpty(Content))
                        throw new InvalidOperationException("Cannot self-close a tag with non-empty content.");
                    Content = null;
                }
                else if (Content == null)
                {
                    Content = "";
                }
            }
        }

        protected override string GetContentString() => Content;
    }

    public class HtmlTag : TagNode
    {
        /// <summary>
        /// Content of the tag.
        /// </summary>
        /// <value>Content of the tag, as <see cref="Wikitext"/>. If the tag is self-closing, the value is <c>null</c>.</value>
        public Wikitext Content { get; set; }

        protected override Node CloneCore()
        {
            var n = new HtmlTag
            {
                Name = Name,
                ClosingTagName = ClosingTagName,
                Content = Content,
                TrailingWhitespace = TrailingWhitespace,
                ClosingTagTrailingWhitespace = ClosingTagTrailingWhitespace,
            };
            n.Attributes.Add(Attributes);
            return n;
        }

        /// <summary>
        /// Whether the tag is self closed. E.g. &lt;references /&gt;.
        /// </summary>
        public override bool IsSelfClosing
        {
            get { return Content == null; }
            set
            {
                if (value)
                {
                    if (Content != null && !Content.Lines.IsEmpty)
                        throw new InvalidOperationException("Cannot self-close a tag with non-empty content.");
                    Content = null;
                }
                else if (Content == null)
                {
                    Content = new Wikitext();
                }
            }
        }

        protected override string GetContentString() => Content?.ToString();
    }

    public class TagAttribute : Node
    {
        private string _LeadingWhitespace;

        public Run Name { get; set; }

        public Wikitext Value { get; set; }

        /// <summary>
        /// The whitespace before the property expression.
        /// </summary>
        /// <exception cref="ArgumentException">The string contains non-white-space characters. OR The string is <c>null</c> or empty.</exception>
        public string LeadingWhitespace
        {
            get { return _LeadingWhitespace; }
            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Only white spaces are accepted.", nameof(value));
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("Null or empty string is not accepted.", nameof(value));
                _LeadingWhitespace = value;
            }
        }

        protected override Node CloneCore()
        {
            return new TagAttribute {Name = Name, Value = Value};
        }

        public override string ToString() => LeadingWhitespace + Name + "=" + Value;
    }

    public class Comment : InlineNode
    {
        public Comment() : this(null)
        {
        }

        public Comment(string content)
        {
            Content = content;
        }

        public string Content { get; set; }

        protected override Node CloneCore()
        {
            return new Comment {Content = Content};
        }

        public override string ToString() => "<!--" + Content + "-->";
    }
}
