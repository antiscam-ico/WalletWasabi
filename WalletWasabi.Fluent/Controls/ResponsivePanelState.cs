﻿using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;

namespace WalletWasabi.Fluent.Controls
{
	internal class ResponsivePanelState
	{
		internal IReadOnlyList<ILayoutable> Children;
		internal double ItemWidth;
		internal double ItemHeight;
		internal double AspectRatio;
		internal AvaloniaList<int> ColumnHints;
		internal AvaloniaList<double> WidthTriggers;
		internal double Width;
		internal double Height;

		private struct Item
		{
			internal int Column;
			internal int Row;
			internal int ColumnSpan;
			internal int RowSpan;
		}

		internal bool Validate()
		{
			if (WidthTriggers is null || ColumnHints is null)
			{
				return false;
			}

			if (WidthTriggers.Count <= 0)
			{
				// TODO: throw new Exception($"No width trigger specified in {nameof(WidthTriggers)} property.");
				return false;
			}

			if (ColumnHints.Count <= 0)
			{
				// No column hints specified in ColumnHints property.
				return false;
			}

			if (WidthTriggers.Count != ColumnHints.Count)
			{
				// Number of width triggers must be equal to the number of column triggers.
				return false;
			}

			if (double.IsNaN(ItemWidth) && double.IsInfinity(Width))
			{
				// The ItemWidth can't be NaN and panel width can't be infinity at same time.
				return false;
			}

			if (double.IsNaN(ItemHeight) && double.IsInfinity(Height))
			{
				// The ItemHeight can't be NaN and panel height can't be infinity at same time.
				return false;
			}

			return true;
		}

		internal Size MeasureArrange(bool isMeasure)
		{
			var layoutIndex = 0;
			var totalColumns = ColumnHints[layoutIndex];

			if (!double.IsInfinity(Width))
			{
				for (var i = WidthTriggers.Count - 1; i >= 0; i--)
				{
					if (Width >= WidthTriggers[i])
					{
						totalColumns = ColumnHints[i];
						layoutIndex = i;
						break;
					}
				}
			}

			var currentColumn = 0;
			var totalRows = 0;
			var rowIncrement = 1;
			var items = new Item[Children.Count];

			for (var i = 0; i < Children.Count; i++)
			{
				var element = Children[i];
				var columnSpan = ResponsivePanel.GetColumnSpan((Control) element)[layoutIndex];
				var rowSpan = ResponsivePanel.GetRowSpan((Control) element)[layoutIndex];

				items[i] = new Item()
				{
					Column = currentColumn,
					Row = totalRows,
					ColumnSpan = columnSpan,
					RowSpan = rowSpan
				};

				rowIncrement = Math.Max(rowSpan, rowIncrement);
				currentColumn += columnSpan;

				if (currentColumn >= totalColumns || i == Children.Count - 1)
				{
					currentColumn = 0;
					totalRows += rowIncrement;
					rowIncrement = 1;
				}
			}

			var columnWidth = double.IsNaN(ItemWidth) ? Width / totalColumns : ItemWidth;
			var rowHeight = double.IsNaN(ItemHeight)
				? double.IsNaN(AspectRatio) ? Height / totalRows : columnWidth * AspectRatio
				: ItemHeight;

			for (var i = 0; i < Children.Count; i++)
			{
				var element = Children[i];
				var size = new Size(columnWidth * items[i].ColumnSpan, rowHeight * items[i].RowSpan);
				var position = new Point(items[i].Column * columnWidth, items[i].Row * rowHeight);
				var rect = new Rect(position, size);

				if (isMeasure)
				{
					element.Measure(size);
				}
				else
				{
					element.Arrange(rect);
				}
			}

			return new Size(columnWidth * totalColumns, rowHeight * totalRows);
		}
	}
}