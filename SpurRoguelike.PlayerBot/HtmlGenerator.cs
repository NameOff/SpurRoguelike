using System.Collections.Generic;
using System.IO;
using System.Text;
using SpurRoguelike.Core.Primitives;
using SpurRoguelike.Core.Views;

namespace SpurRoguelike.PlayerBot
{
    class HtmlGenerator
    {

            private const string Template =
                "<html>\r\n<head>\r\n<link rel=\"stylesheet\" href=\"style.css\">\r\n</head>\r\n\r\n<body>\r\n\r\n<table>{0}</table>\r\n\r\n</body>\r\n\r\n</html>";


            private const string Health = "Health";
            private const string Item = "Item";
            private const string Player = "Player";

            private static string OpenTd(string className)
            {
                return $"<td class='{className}'>";
            }
            private static string CloseTd(int value)
            {
                return $"{value}</td>";
            }



            public static string Generate(LevelView levelView, Dictionary<Location, int> costs)
            {
                var table = new string[levelView.Field.Width, levelView.Field.Height];

                for (var x = 0; x < table.GetLength(0); x++)
                {
                    for (var y = 0; y < table.GetLength(1); y++)
                    {
                        table[x, y] = OpenTd(levelView.Field[new Location(x, y)].ToString());
                    }
                }

                foreach (var monster in levelView.Monsters)
                {
                    table[monster.Location.X, monster.Location.Y] = OpenTd(monster.GetType().Name);
                }

                foreach (var healthPackView in levelView.HealthPacks)
                {
                    table[healthPackView.Location.X, healthPackView.Location.Y] = OpenTd(Health);
                }

                foreach (var item in levelView.Items)
                {
                    table[item.Location.X, item.Location.Y] = OpenTd(Item);
                }

                table[levelView.Player.Location.X, levelView.Player.Location.Y] = OpenTd(Player);

                for (var x = 0; x < table.GetLength(0); x++)
                {
                    for (var y = 0; y < table.GetLength(1); y++)
                    {
                        var value = costs.ContainsKey(new Location(x, y)) ? costs[new Location(x, y)] : 0;
                        table[x, y] += CloseTd(value);
                    }
                }

                var text = new StringBuilder();


                for (var y = 0; y < table.GetLength(1); y++)
                {
                    text.Append("<tr>");
                    for (var x = 0; x < table.GetLength(0); x++)
                    {
                        text.Append(table[x, y]);
                    }
                    text.Append("</tr>");
                }


                return string.Format(Template, text);

            }

            public static void WriteHtml(LevelView levelView, Dictionary<Location, int> costs, string pathToHtml)
            {
                File.WriteAllText(pathToHtml, Generate(levelView, costs));
            }
        }
}
