using ChessChallenge.API;
using System;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class LastBot
    {
        public static IChessBot GetLastBot()
        {
            return new MyBotV1_3();
        }
    }
}