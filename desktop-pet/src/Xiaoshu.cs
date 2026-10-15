using System.Collections.Generic;

namespace DesktopPet
{
    // 小糬: the VTuber-style character. Art and voice clips come from assets/xiaoshu (see assets/README.md);
    // the clip id for each line is the key below, so a clip named "click1.wav" is played for that line.
    static class Xiaoshu
    {
        static readonly string[,] Script =
        {
            { "greeting", "嗨嗨～我是小糬！今天也請多指教喔！" },
            { "talk1", "你在忙什麼呀？" },
            { "talk2", "記得喝水喔～" },
            { "talk3", "眼睛累了就休息一下吧！" },
            { "talk4", "嘿嘿，陪我聊天嘛～" },
            { "talk5", "今天也辛苦了！" },
            { "talk6", "好無聊喔……來玩嘛！" },
            { "click1", "呀！好癢！" },
            { "click2", "嘿嘿，喜歡你～" },
            { "click3", "再摸一下嘛！" },
            { "drag1", "哇啊啊！放我下來啦！" },
            { "drag2", "好、好高喔！" },
            { "hurt", "嗚……好痛……" },
            { "hungry1", "肚子餓餓的……" },
            { "hungry2", "想吃麻糬……" },
            { "eating", "嚼嚼嚼……" },
            { "ate", "好好吃！謝謝你！" },
            { "full", "吃不下了啦～" },
            { "sleepy", "好睏喔……晚安……" },
            { "wake", "嗯……早安……" },
        };

        public static Character TryCreate()
        {
            Dictionary<string, string> text = new Dictionary<string, string>();
            Dictionary<string, string> clipByText = new Dictionary<string, string>();
            for (int i = 0; i < Script.GetLength(0); i++)
            {
                text[Script[i, 0]] = Script[i, 1];
                clipByText[Script[i, 1]] = Script[i, 0];
            }

            PetLines lines = new PetLines
            {
                Greeting = text["greeting"],
                Talk = new[] { text["talk1"], text["talk2"], text["talk3"], text["talk4"], text["talk5"], text["talk6"] },
                Click = new[] { text["click1"], text["click2"], text["click3"] },
                Drag = new[] { text["drag1"], text["drag2"] },
                Hungry = new[] { text["hungry1"], text["hungry2"] },
                FoodMenu = "餵食麻糬",
                Eating = text["eating"],
                Ate = text["ate"],
                Full = text["full"],
                Hurt = text["hurt"],
                Sleepy = text["sleepy"],
                Wake = text["wake"],
            };
            return SpriteCharacter.TryLoad("xiaoshu", "小糬", lines, clipByText, 400f, true);
        }
    }
}
