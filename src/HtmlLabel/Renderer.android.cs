using System;
using System.ComponentModel;
using Android.OS;
using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Widget;
using Java.Lang;
using LabelHtml.Forms.Plugin.Abstractions;
using LabelHtml.Forms.Plugin.Droid;
using Org.Xml.Sax;
using Xamarin.Forms;
using Xamarin.Forms.Internals;
using Xamarin.Forms.Platform.Android;

[assembly: ExportRenderer(typeof(HtmlLabel), typeof(HtmlLabelRenderer))]
// ReSharper disable once CheckNamespace
namespace LabelHtml.Forms.Plugin.Droid
{
    /// <summary>
    /// HtmlLable Implementation
    /// </summary>
    [Preserve(AllMembers = true)]
    public class HtmlLabelRenderer : LabelRenderer
    {
		/// <summary>
		/// 
		/// </summary>
		/// <param name="context"></param>
		public HtmlLabelRenderer(Android.Content.Context context) : base(context) { }
		
	    /// <summary>
	    /// Used for registration with dependency service
	    /// </summary>
	    public static void Initialize() { }

		/// <summary>
		/// 
		/// </summary>
		/// <param name="e"></param>
		protected override void OnElementChanged(ElementChangedEventArgs<Label> e)
		{
			base.OnElementChanged(e);

			if (Control == null) return;

			UpdateText();
		}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);

            if (e.PropertyName == Label.TextProperty.PropertyName ||
                     e.PropertyName == Label.FontAttributesProperty.PropertyName ||
                     e.PropertyName == Label.FontFamilyProperty.PropertyName ||
                     e.PropertyName == Label.FontSizeProperty.PropertyName ||
                     e.PropertyName == Label.HorizontalTextAlignmentProperty.PropertyName ||
                     e.PropertyName == Label.TextColorProperty.PropertyName)
                UpdateText();
        }

		private void UpdateText()
		{
			if (Control == null || Element == null) return;
			if (string.IsNullOrEmpty(Control.Text)) return;

			// Gets the complete HTML string
			var customHtml = new LabelRendererHelper(Element, Control.Text).ToString();
			// Android's TextView doesn't handle <ul>s, <ol>s and <li>s 
			// so it replaces them with <ulc>, <olc> and <lic> respectively.
			// Those tags will be handles by a custom TagHandler
			customHtml = customHtml
                .Replace("ul>", "ulc>", StringComparison.Ordinal)
                .Replace("ol>", "olc>", StringComparison.Ordinal)
                .Replace("li>", "lic>", StringComparison.Ordinal);

			Control.SetIncludeFontPadding(false);

			SetTextViewHtml(Control, customHtml);
		}

		private void SetTextViewHtml(TextView text, string html)
		{
			// Tells the TextView that the content is HTML and adds a custom TagHandler
			var sequence = Build.VERSION.SdkInt >= BuildVersionCodes.N ?
				Html.FromHtml(html, FromHtmlOptions.ModeCompact, null, new ListTagHandler()) :
#pragma warning disable 618
				Html.FromHtml(html, null, new ListTagHandler());
#pragma warning restore 618

			// Makes clickable links
			text.MovementMethod = LinkMovementMethod.Instance;
			var strBuilder = new SpannableStringBuilder(sequence);
			var urls = strBuilder.GetSpans(0, sequence.Length(), Class.FromType(typeof(URLSpan)));
			foreach (var span in urls)
				MakeLinkClickable(strBuilder, (URLSpan)span);

			// Android adds an unnecessary "\n" that must be removed
			var value = RemoveLastChar(strBuilder);

			// Finally sets the value of the TextView 
			text.SetText(value, TextView.BufferType.Spannable);
		}

	    private void MakeLinkClickable(ISpannable strBuilder, URLSpan span)
		{
			var start = strBuilder.GetSpanStart(span);
			var end = strBuilder.GetSpanEnd(span);
			var flags = strBuilder.GetSpanFlags(span);
			var clickable = new MyClickableSpan((HtmlLabel)Element, span);
			strBuilder.SetSpan(clickable, start, end, flags);
			strBuilder.RemoveSpan(span);
		}

		private class MyClickableSpan : ClickableSpan
		{
			private readonly HtmlLabel _label;
			private readonly URLSpan _span;

			public MyClickableSpan(HtmlLabel label, URLSpan span)
			{
				_label = label;
				_span = span;
			}

			public override void OnClick(global::Android.Views.View widget)
			{
				var args = new WebNavigatingEventArgs(WebNavigationEvent.NewPage, new UrlWebViewSource { Url = _span.URL }, _span.URL);
				_label.SendNavigating(args);

				if (args.Cancel)
					return;

				Device.OpenUri(new Uri(_span.URL));
				_label.SendNavigated(args);
			}
		}

		private static ISpanned RemoveLastChar(ICharSequence text)
		{
			var builder = new SpannableStringBuilder(text);
			if (text.Length() != 0)
				builder.Delete(text.Length() - 1, text.Length());
			return builder;
		}
	}

	// TagHandler that handles lists (ul, ol)
	internal class ListTagHandler : Java.Lang.Object, Html.ITagHandler
	{
		private ListBuilder _listBuilder = new ListBuilder();

		public void HandleTag(bool opening, string tag, IEditable output, IXMLReader xmlReader)
		{
			tag = tag.ToUpperInvariant();
			if (tag.Equals("LIC", StringComparison.Ordinal))
			{
				_listBuilder.Li(opening, output);
				return;
			}
			if (tag.Equals("OLC", StringComparison.Ordinal) || tag.Equals("ULC", StringComparison.Ordinal))
			{
				if (opening)
				{
					_listBuilder = _listBuilder.StartList(tag[0] == 'o', output);
				}
				else
				{
					_listBuilder = _listBuilder.CloseList(output);
				}
				return;
			}
		}
	}

	internal class ListBuilder
	{
		public const int LIST_INTEND = 20;
		private int _liIndex = -1;
		private int _liStart = -1;
		private LiGap _liGap;
		private int _gap = 0;

		private ListBuilder _parent = null;

		internal ListBuilder() : this(null)
		{
		}

		internal ListBuilder(LiGap liGap)
		{
			_parent = null;
			_gap = 0;
			if (liGap != null)
			{
				_liGap = liGap;
			}
			else
			{
				_liGap = GetLiGap(null);
			}
		}

		private ListBuilder(ListBuilder parent, bool ordered)
		{
			_parent = parent;
			_liGap = parent._liGap;
			_gap = parent._gap + LIST_INTEND + _liGap.GetGap(ordered);
			_liIndex = ordered ? 0 : -1;
		}

		internal ListBuilder StartList(bool ordered, IEditable output)
		{
			if (_parent == null)
			{
				if (output.Length() > 0) output.Append("\n ");
			}
			return new ListBuilder(this, ordered);
		}

		private bool IsOrdered()
		{
			return _liIndex >= 0;
		}

		internal void Li(bool opening, IEditable output)
		{
			if (opening)
			{
				EnsureParagraphBoundary(output);
				_liStart = output.Length();

				if (IsOrdered())
				{
					output.Append(++_liIndex + ". ");
				}
				else
				{
					output.Append("•  ");
				}
			}
			else
			{
				if (_liStart >= 0)
				{
					EnsureParagraphBoundary(output);
					output.SetSpan(new LeadingMarginSpanStandard(_gap - _liGap.GetGap(IsOrdered()), _gap), _liStart, output.Length(), SpanTypes.ExclusiveExclusive);
					_liStart = -1;
				}
			}
		}


		internal ListBuilder CloseList(IEditable output)
		{
			EnsureParagraphBoundary(output);
			var result = _parent;
			if (result == null) result = this;
			if (result._parent == null) output.Append('\n');
			return result;
		}

		private static void EnsureParagraphBoundary(IEditable output)
		{
			if (output.Length() == 0) return;
			char lastChar = output.CharAt(output.Length() - 1);
			if (lastChar != '\n') output.Append('\n');
		}

		internal class LiGap
		{
			private readonly int _orderedGap;
			private readonly int _unorderedGap;

			internal LiGap(int orderedGap, int unorderedGap)
			{
				_orderedGap = orderedGap;
				_unorderedGap = unorderedGap;
			}

			public int GetGap(bool ordered)
			{
				return ordered ? _orderedGap : _unorderedGap;
			}
		}

		internal static LiGap GetLiGap(TextView tv)
		{
			if (tv == null)
			{
				return new LiGap(40, 30);
			}
			return new LiGap(ComputeWidth(tv, true), ComputeWidth(tv, false));
		}

		private static int ComputeWidth(TextView tv, bool ordered)
		{
			Android.Graphics.Paint paint = tv.Paint;

			//paint.setTypeface(tv.getPaint().getTypeface());
			//paint.setTextSize(tv.getPaint().getTextSize());

			// Now compute!
			var bounds = new Android.Graphics.Rect();
			string myString = ordered ? "99. " : "• ";
			paint.GetTextBounds(myString, 0, myString.Length, bounds);
			int width = bounds.Width();
			float pt = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Pt, width, tv.Context.Resources.DisplayMetrics);
			float sp = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Sp, width, tv.Context.Resources.DisplayMetrics);
			float dip = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Dip, width, tv.Context.Resources.DisplayMetrics);
			float px = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Px, width, tv.Context.Resources.DisplayMetrics);
			float mm = Android.Util.TypedValue.ApplyDimension(Android.Util.ComplexUnitType.Mm, width, tv.Context.Resources.DisplayMetrics);
			return (int)pt;
		}

	}
}
