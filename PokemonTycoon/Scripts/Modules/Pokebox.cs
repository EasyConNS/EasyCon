using PokemonTycoon.Graphic;
using PokemonTycoon.Scripts.PokemonFilter;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PokemonTycoon.Scripts
{
    public class Pokebox : ScriptCore
    {
        const int DefaultCursorWait = 200;
        public const int MAX_X = 6;
        public const int MAX_Y = 5;
        public const int MAX_L = Pokelist.MAX_LENGTH;

        public bool Check()
        {
            Log($"Pokebox.Check()");
            return VideoCapture.Match(48, 28, ImageRes.Get("pokebox"), DefaultImageCap);
        }

        public enum SummaryPage
        {
            None,
            Status,
            IV,
        }

        public SummaryPage GetSummaryPage()
        {
            Log($"Pokebox.GetSummaryPage()");
            if (!VideoCapture.Match(1473, 338, Color.FromArgb(218, 218, 218), DefaultColorCap))
                return SummaryPage.None;
            if (VideoCapture.Match(1723, 1054, Color.FromArgb(254, 254, 254), DefaultColorCap))
                return SummaryPage.Status;
            return SummaryPage.IV;
        }

        public bool IsEmptySlot()
        {
            return VideoCapture.Match(1675, 1039, Color.FromArgb(0, 0, 0), DefaultColorCap);
        }

        public List<int> GetIVs()
        {
            Log($"Pokebox.GetIVs()");
            List<int> list = new List<int>(6);
            for (int i = 0; i < 6; i++)
            {
                int x = 1500;
                int y = 225 + i * 56;
                int iv = -1;
                if (VideoCapture.Match(x, y, ImageRes.Get("pokebox_iv31"), DefaultImageCap, VideoCapture.LineSampler(3)))
                    iv = 31;
                else if (VideoCapture.Match(x, y, ImageRes.Get("pokebox_iv0"), DefaultImageCap, VideoCapture.LineSampler(3)))
                    iv = 0;
                list.Add(iv);
            }
            Log(list);
            return list;
        }

        public Pokemon.Gender GetGender()
        {
            if (VideoCapture.SearchColor(1380, 60, 320, 1, Color.FromArgb(215, 0, 11), DefaultColorCap).Count > 0)
                return Pokemon.Gender.Female;
            if (VideoCapture.SearchColor(1380, 60, 320, 1, Color.FromArgb(18, 62, 227), DefaultColorCap).Count > 0)
                return Pokemon.Gender.Male;
            return Pokemon.Gender.None;
        }

        public static Bitmap GetAbilityImage(string abilityName)
        {
            return ImageRes.Get($"ab_{abilityName}");
        }

        public bool CheckAbility(Bitmap abilityImage)
        {
            return VideoCapture.Match(1500, 618, abilityImage, DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool CheckAbility(string abilityName)
        {
            return CheckAbility(GetAbilityImage(abilityName));
        }

        public bool IsShiny()
        {
            Log($"Pokebox.IsShiny()");
            return VideoCapture.Match(1880, 170, ImageRes.Get("pokebox_shiny"), DefaultImageCap);
        }

        internal bool CheckFilter(Filter filter, out bool isShiny)
        {
            isShiny = false;
            if (IsEmptySlot())
                return false;
            Bool3VL shiny = Bool3VL.Unknown;
            filter.Reset();
            // check pokemon
            Wait(200);
            var page0 = Module<Pokebox>().GetSummaryPage();
            var page = page0;
            while (true)
            {
                try
                {
                    VideoCapture.Freeze();
                    // check shiny
                    if (shiny.IsUnknown && (page == Pokebox.SummaryPage.Status || page == Pokebox.SummaryPage.IV))
                        shiny = Module<Pokebox>().IsShiny();
                    // check filter
                    filter.Check(this, page);
                }
                finally
                {
                    VideoCapture.Unfreeze();
                }
                if (shiny.IsKnown && filter.Result.IsKnown)
                    break;
                // next page
                Press(Keys.PLUS);
                Wait(500);
                page = Module<Pokebox>().GetSummaryPage();
            }
            isShiny = shiny.Boolean;
            return isShiny || filter.Result.Boolean;
        }

        internal bool CheckFilter(Filter filter)
        {
            bool isShiny;
            return CheckFilter(filter, out isShiny);
        }

        public enum Section
        {
            None,
            Party,
            Box,
            PartyTitle,
            BoxTitle,
            BoxList,
            Search,
        }

        public struct Cursor
        {
            public readonly Section Section;
            public readonly int X;
            public readonly int Y;

            public Cursor(Section section, int x, int y)
            {
                Section = section;
                X = x;
                Y = y;
            }

            public Cursor(Section section)
            {
                Section = section;
                X = 0;
                Y = 0;
            }

            public override string ToString()
            {
                return string.Join(",", Section.GetName(), X, Y);
            }
        }

        public Cursor GetCursor()
        {
            Log($"Pokebox.GetCursor()");
            try
            {
                VideoCapture.Freeze();
                if (VideoCapture.Match(523, 960, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return new Cursor(Section.BoxList);
                if (VideoCapture.Match(902, 960, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return new Cursor(Section.Search);
                if (VideoCapture.Match(59, 132, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return new Cursor(Section.PartyTitle);
                if (VideoCapture.Match(663, 116, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return new Cursor(Section.BoxTitle);
                for (int y = 0; y < MAX_L; y++)
                    if (VideoCapture.Match(112, 232 + 136 * y, Color.FromArgb(235, 40, 76), DefaultColorCap)
                        || VideoCapture.Match(112, 232 + 136 * y, Color.FromArgb(14, 136, 222), DefaultColorCap)
                        || VideoCapture.Match(112, 232 + 136 * y, Color.FromArgb(19, 236, 122), DefaultColorCap))
                        return new Cursor(Section.Party, 0, y);
                for (int x = 0; x < MAX_X; x++)
                    for (int y = 0; y < MAX_Y; y++)
                        if (VideoCapture.Match(546 + 136 * x, 237 + 136 * y, Color.FromArgb(235, 40, 76), DefaultColorCap)
                            || VideoCapture.Match(546 + 136 * x, 237 + 136 * y, Color.FromArgb(14, 136, 222), DefaultColorCap)
                            || VideoCapture.Match(546 + 136 * x, 237 + 136 * y, Color.FromArgb(19, 236, 122), DefaultColorCap))
                            return new Cursor(Section.Box, x, y);
                return new Cursor();
            }
            finally
            {
                VideoCapture.Unfreeze();
            }
        }

        public enum CursorMode
        {
            Normal = 0,
            Swap = 1,
            Multi = 2,
        }

        public CursorMode GetCursorMode()
        {
            if (VideoCapture.Match(85, 0, Color.FromArgb(51, 51, 51), DefaultColorCap))
                return CursorMode.Normal;
            if (VideoCapture.Match(495, 0, Color.FromArgb(51, 51, 51), DefaultColorCap))
                return CursorMode.Multi;
            return CursorMode.Swap;
        }

        public void SetCursorMode(CursorMode mode)
        {
            int n = ((int)mode + 3 - (int)GetCursorMode()) % 3;
            for (int i = 0; i < n; i++)
            {
                Press(Keys.Y);
                Wait(DefaultCursorWait);
            }
        }

        bool _ToBox(Cursor cursor, int x, int y)
        {
            if (cursor.Section == Section.Party)
            {
                Press(Keys.HAT.Right);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.Box)
            {
                if (x != -1)
                {
                    if (x >= cursor.X + 4)
                        x -= 7;
                    if (x <= cursor.X - 4)
                        x += 7;
                    for (int i = 0; i < cursor.X - x; i++)
                    {
                        Press(Keys.HAT.Left);
                        Wait(DefaultCursorWait);
                    }
                    for (int i = 0; i < x - cursor.X; i++)
                    {
                        Press(Keys.HAT.Right);
                        Wait(DefaultCursorWait);
                    }
                }
                if (y != -1)
                {
                    if (y >= cursor.Y + 4)
                        y -= 7;
                    if (y <= cursor.Y - 4)
                        y += 7;
                    for (int i = 0; i < cursor.Y - y; i++)
                    {
                        Press(Keys.HAT.Up);
                        Wait(DefaultCursorWait);
                    }
                    for (int i = 0; i < y - cursor.Y; i++)
                    {
                        Press(Keys.HAT.Down);
                        Wait(DefaultCursorWait);
                    }
                }
                return true;
            }
            else if (cursor.Section == Section.PartyTitle)
            {
                Press(Keys.HAT.Down);
                Wait(DefaultCursorWait);
                Press(Keys.HAT.Right);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.BoxTitle)
            {
                Press(Keys.HAT.Down);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.BoxList)
            {
                Press(Keys.HAT.Up);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.Search)
            {
                Press(Keys.HAT.Up);
                Wait(DefaultCursorWait);
                return false;
            }
            else
            {
                Wait(1000);
                return false;
            }
        }

        bool _ToList(Cursor cursor, int index)
        {
            if (cursor.Section == Section.Party)
            {
                for (int i = 0; i < cursor.Y - index; i++)
                {
                    Press(Keys.HAT.Up);
                    Wait(DefaultCursorWait);
                }
                for (int i = 0; i < index - cursor.Y; i++)
                {
                    Press(Keys.HAT.Down);
                    Wait(DefaultCursorWait);
                }
                return true;
            }
            else if (cursor.Section == Section.Box)
            {
                for (int i = 0; i < cursor.X + 1; i++)
                {
                    Press(Keys.HAT.Left);
                    Wait(DefaultCursorWait);
                }
                return false;
            }
            else if (cursor.Section == Section.PartyTitle)
            {
                Press(Keys.HAT.Down);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.BoxTitle)
            {
                Press(Keys.HAT.Down);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.BoxList)
            {
                Press(Keys.HAT.Up);
                Wait(DefaultCursorWait);
                return false;
            }
            else if (cursor.Section == Section.Search)
            {
                Press(Keys.HAT.Up);
                Wait(DefaultCursorWait);
                return false;
            }
            else
            {
                Wait(1000);
                return false;
            }
        }

        public void SelectBox(int x, int y)
        {
            Log($"Pokebox.SelectBox({x},{y})");
            while (true)
            {
                var cursor = GetCursor();
                if (_ToBox(cursor, x, y))
                    return;
            }
        }

        public void SelectList(int index)
        {
            Log($"Pokebox.SelectList({index})");
            while (true)
            {
                var cursor = GetCursor();
                if (_ToList(cursor, index))
                    return;
            }
        }

        public void SelectBoxList()
        {
            Log($"Pokebox.SelectBoxList()");
            while (true)
            {
                var cursor = GetCursor();
                if (cursor.Section == Section.BoxList)
                    return;
                else if (cursor.Section == Section.Search)
                {
                    Press(Keys.HAT.Left);
                    Wait(DefaultCursorWait);
                }
                else if (_ToBox(cursor, -1, MAX_Y - 1))
                {
                    Press(Keys.HAT.Down);
                    Wait(DefaultCursorWait);
                    return;
                }
            }
        }

        public void SelectNext(ref int x, ref int y)
        {
            Log($"Pokebox.SelectNext({x},{y})");
            var cursor = new Cursor(Section.Box, x, y);
            if (x == MAX_X - 1 && y == MAX_Y - 1)
            {
                // next box
                Press(Keys.R);
                Wait(DefaultCursorWait);
                x = 0;
                y = 0;
                _ToBox(cursor, x, y);
            }
            else
            {
                // next slot
                x++;
                if (x >= MAX_X)
                {
                    x -= MAX_X;
                    y++;
                }
                _ToBox(cursor, x, y);
            }
            Wait(200);
        }

        public void SelectNext()
        {
            Log($"Pokebox.SelectNext()");
            var cursor = GetCursor();
            if (cursor.Section != Section.Box)
            {
                SelectBox(0, 0);
                return;
            }
            int x = cursor.X;
            int y = cursor.Y;
            SelectNext(ref x, ref y);
        }

        public void Release()
        {
            Log($"Pokebox.Release()");
            Press(Keys.A);
            Wait(500);
            if (Module<Release>().Check())
            {
                Press(Keys.HAT.Up);
                Wait(300);
                Press(Keys.HAT.Up);
                Wait(300);
                Press(Keys.A);
                Wait(1200);
                Press(Keys.HAT.Up);
                Wait(300);
                Press(Keys.A);
                Wait(1800);
                Press(Keys.A);
                Wait(500);
            }
        }

        public class Boxlist : ScriptCore
        {

            public bool Check()
            {
                Log($"Pokebox.Boxlist.Check()");
                return VideoCapture.Match(1763, 789, ImageRes.Get("pokebox_boxlist"), DefaultImageCap, VideoCapture.LineSampler(3));
            }

            public enum Section
            {
                None,
                Box,
                BoxList,
                Search,
            }

            public enum BoxState
            {
                Empty,
                Normal,
                Full,
            }

            public struct Cursor
            {
                public readonly Section Section;
                public readonly int X;
                public readonly int Y;

                public Cursor(Section section, int x, int y)
                {
                    Section = section;
                    X = x;
                    Y = y;
                }

                public Cursor(Section section)
                {
                    Section = section;
                    X = 0;
                    Y = 0;
                }

                public override string ToString()
                {
                    return string.Join(",", Section.GetName(), X, Y);
                }
            }

            public Cursor GetCursor()
            {
                Log($"Pokebox.Boxlist.GetCursor()");
                try
                {
                    VideoCapture.Freeze();
                    if (VideoCapture.Match(600, 957, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        return new Cursor(Section.BoxList);
                    if (VideoCapture.Match(979, 960, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        return new Cursor(Section.Search);
                    for (int x = 0; x < 8; x++)
                        for (int y = 0; y < 4; y++)
                            if (VideoCapture.Match(946 + 122 * x, 298 + 154 * y, Color.FromArgb(235, 40, 76), DefaultColorCap))
                                return new Cursor(Section.Box, x, y);
                    return new Cursor();
                }
                finally
                {
                    VideoCapture.Unfreeze();
                }
            }
        }
    }
}
