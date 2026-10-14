using DungeonForge.Web.Models;

namespace DungeonForge.Web.Components;

// Hand-made 2-frame scenes keyed by the narrator's scene tag (see SceneTags on the API side). Keep each frame <= 7 lines x 26 cols.
public static class AsciiArt
{
    private static readonly Dictionary<string, string[]> Scenes = new()
    {
        ["goblin"] =
        [
            """
               ,___,
               (o,o)    |\
               /)_)\----+-
                " "     |/
            """,
            """
               ,___,
               (o,o)   \
               /)_)\----+>
                " "    /
            """,
        ],
        ["skeleton"] =
        [
            """
                .-.
               (o o)
                |=|    /
               /|||\  /
                |||
               _/ \_
            """,
            """
                .-.
               (o o)  \
                |=|    \
               /|||\    |
                |||
               _/ \_
            """,
        ],
        ["rat"] =
        [
            """
               ,-.__
              (  o  ""-.__,~
               `---'  ;;;;
            """,
            """
               ,-.__
              (  o  ""-.__,~
               `---' ;;;;
            """,
        ],
        ["dragon"] =
        [
            """
                  /|    /|
                 / |___/ |
                (  o   o  )
                 \  ===  /  ~~~
                  \_____/
            """,
            """
                  /|    /|
                 / |___/ |
                (  O   O  )  ~~~~~
                 \  ===  / ~~~~~~
                  \_____/   ~~~~
            """,
        ],
        ["ghost"] =
        [
            """
                .-.
               (o o)
               | O |
               |   |
               '~~~'
            """,
            """
                .-.
               (o o)
               | O |
               |   |
               '~'~'
            """,
        ],
        ["chest"] =
        [
            """
                ______
               /_____/|
               |=[]==| |
               |_____|/
            """,
            """
              *  ____  *
               /_____/
               |=[]==|*
               |_____|
            """,
        ],
        ["door"] =
        [
            """
               .-----.
               |  _  |
               | | | |
               | |o| |
               | |_| |
               '-----'
            """,
            """
               .-----.
               |  _/ |
               | | / |
               | |o/ |
               | |_/ |
               '-----'
            """,
        ],
        ["lever"] =
        [
            """
                  o
                  |
               ___|___
              [_______]
            """,
            """
                    o
                   /
               ___/___
              [_______]
            """,
        ],
        ["trap"] =
        [
            """
               _.-._.-._.-._
               |^|^|^|^|^|^|
               `-'-'-'-'-'-'
            """,
            """
               _.-._.-._.-._
               | | | | | | |
               `-'-'-'-'-'-'
            """,
        ],
        ["altar"] =
        [
            """
                 *
                ( )
               _|_|_
              |_____|
              |_____|
            """,
            """
                 .
                (*)
               _|_|_
              |_____|
              |_____|
            """,
        ],
        ["generic"] =
        [
            """
                )
               ( )
                |
               _|_
            """,
            """
                (
               ( )
                |
               _|_
            """,
        ],
    };

    // Unknown or missing tags fall back to a scene that fits the room type.
    public static string[] Frames(string? scene, RoomType? room) => Scenes[Tag(scene, room)];

    public static string Tag(string? scene, RoomType? room) =>
        scene is not null && Scenes.ContainsKey(scene) ? scene : room switch { RoomType.Combat => "goblin", RoomType.Boss => "dragon", RoomType.Chest => "chest", RoomType.Puzzle => "lever", _ => "generic" };
}
