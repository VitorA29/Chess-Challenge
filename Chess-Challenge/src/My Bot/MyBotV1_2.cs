using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

public class MyBotV1_2 : IChessBot
{
    private readonly Dictionary<ulong, BoardNode> _searchMap = new();
    private readonly Queue<IEnumerable<Move>> _searchQueue = new();
    private const int _maxSearchDepth = 3;
    private const int _searchTimeout = 5000;
    private readonly Random _random = new(1009);

    private struct BoardNode
    {
        private readonly ulong _zobristKey;
        private readonly bool _isWhiteToMove;
        private readonly string _boardFen;

        public double Value;
        public HashSet<Move> BestMoves = new();
        public bool IsSearchDone = false;
        public readonly Dictionary<Move, ulong> Transitions = new();

        public BoardNode(Board board)
        {
            _zobristKey = board.ZobristKey;
            _isWhiteToMove = board.IsWhiteToMove;
            _boardFen = board.GetFenString();
            Value = _isWhiteToMove ? int.MinValue : int.MaxValue;
        }

        public BoardNode(Board board, double value, bool isSearchDone)
        {
            _zobristKey = board.ZobristKey;
            _isWhiteToMove = board.IsWhiteToMove;
            _boardFen = board.GetFenString();
            Value = value;
            IsSearchDone = isSearchDone;
        }

        public void UpdateValue(double newValue, bool isSearchDone, Move move, bool removeBadMoves = true)
        {
            if (Value == newValue)
            {
                BestMoves.Add(move);
                IsSearchDone = IsSearchDone && isSearchDone;
            }
            else if ((_isWhiteToMove && Value < newValue)
            || (!_isWhiteToMove && Value > newValue))
            {
                Value = newValue;
                BestMoves.Clear();
                BestMoves.Add(move);
                IsSearchDone = isSearchDone;
            }
            else if (removeBadMoves)
            {
                BestMoves.Remove(move);
                IsSearchDone = IsSearchDone && BestMoves.Count > 0;
            }
        }

        public void ResetValue()
        {
            Value = _isWhiteToMove ? int.MinValue : int.MaxValue;
            BestMoves.Clear();
            IsSearchDone = false;
        }

        public void Test(ulong key, string fen)
        {
            if (_zobristKey == key && _boardFen != fen)
            {
                Console.WriteLine($"Invalid board");
            }
        }

        public void Done(ulong key)
        {
            if (_zobristKey == key && (_isWhiteToMove && Value < 0) || (!_isWhiteToMove && Value > 0))
            {
                Console.WriteLine($"Big shit!!! Board is \"{_boardFen}\"");
            }
            if (BestMoves.Count == 0)
            {
                Console.WriteLine($"Shit value is {Value}");
            }
        }
    }

    // Returns if the game ends.
    private bool EvaluateBoard(Board board, int elapsedTime, out double boardValue, int movesSoFar)
    {
        if (board.IsDraw())
        {
            boardValue = 0;
            return true;
        }

        var searchTreeHeightPenality = (1000 - movesSoFar) / 1000.0;
        if (board.IsInCheckmate())
        {
            var winner = board.IsWhiteToMove ? -1 : 1;
            boardValue = winner * (255 + 1000 * searchTreeHeightPenality);
            return true;
        }

        var pieceLists = board.GetAllPieceLists();
        var boardPiecesValue = pieceLists.Sum(pl => (pl.IsWhitePieceList ? 1 : -1) * pl.Count * (((int)pl.TypeOfPieceInList)) * (((int)pl.TypeOfPieceInList)));
        boardValue = boardPiecesValue * (2 - elapsedTime / 10000.0) * searchTreeHeightPenality;
        return false;
    }

    public Move Think(Board board, Timer timer)
    {
        _searchQueue.Enqueue(new List<Move>());
        var startBoard = board.ZobristKey;
        var startBoardFen = board.GetFenString();
        Console.WriteLine($"Board is \"{board.GetFenString()}\"");
        while (_searchQueue.Count > 0)
        {
            if (timer.MillisecondsElapsedThisTurn >= _searchTimeout)
            {
                _searchQueue.Clear();
                break;
            }
            var currentBoardSetUp = _searchQueue.Dequeue();
            foreach (var realizedMove in currentBoardSetUp)
            {
                board.MakeMove(realizedMove);
            }

            if (_searchMap.TryGetValue(board.ZobristKey, out var node) && node.IsSearchDone)
            {
                foreach (var realizedMove in currentBoardSetUp.Reverse())
                {
                    board.UndoMove(realizedMove);
                }
                continue;
            }

            try
            {
                if (!_searchMap.TryGetValue(board.ZobristKey, out var parentBoardNode))
                {
                    parentBoardNode = new(board);
                    _searchMap.Add(board.ZobristKey, parentBoardNode);
                }
                parentBoardNode.Test(startBoard, startBoardFen);
                parentBoardNode.ResetValue();
                var testChildValues = new List<Tuple<Move, double, bool>>();
                foreach (var previewMove in board.GetLegalMoves())
                {
                    board.MakeMove(previewMove);
                    if (!_searchMap.TryGetValue(board.ZobristKey, out var childNode))
                    {
                        var isEnded = EvaluateBoard(board, timer.MillisecondsElapsedThisTurn, out var nodeValue, board.GameMoveHistory.Length + currentBoardSetUp.Count());
                        childNode = new(board, nodeValue, isEnded);
                        _searchMap.Add(board.ZobristKey, childNode);
                    }

                    if (!childNode.IsSearchDone && currentBoardSetUp.Count() < _maxSearchDepth)
                    {
                        _searchQueue.Enqueue(currentBoardSetUp.Append(previewMove));
                    }
                    parentBoardNode.Transitions.TryAdd(previewMove, board.ZobristKey);
                    parentBoardNode.UpdateValue(childNode.Value, childNode.IsSearchDone, previewMove);
                    testChildValues.Add(Tuple.Create(previewMove, childNode.Value, childNode.IsSearchDone));
                    board.UndoMove(previewMove);
                }
                parentBoardNode.Done(startBoard);
            }
            finally
            {
                foreach (var realizedMove in currentBoardSetUp.Reverse())
                {
                    var childBoardNode = _searchMap[board.ZobristKey];
                    board.UndoMove(realizedMove);
                    var parentBoardNode = _searchMap[board.ZobristKey];
                    parentBoardNode.UpdateValue(childBoardNode.Value, childBoardNode.IsSearchDone, realizedMove, true);
                    if (parentBoardNode.BestMoves.Count == 0)
                    {
                        parentBoardNode.ResetValue();
                        foreach (var (move, zobristKey) in parentBoardNode.Transitions)
                        {
                            var auxChild = _searchMap[zobristKey];
                            parentBoardNode.UpdateValue(auxChild.Value, auxChild.IsSearchDone, move);
                        }
                    }
                    parentBoardNode.Done(startBoard);
                }
            }
        }
        _searchMap.Remove(board.ZobristKey, out var boardNode);
        if ((board.IsWhiteToMove && boardNode.Value < 0) || (!board.IsWhiteToMove && boardNode.Value > 0))
        {
            Console.WriteLine($"{(board.IsWhiteToMove ? "White" : "Black")} value is {boardNode.Value} with {boardNode.BestMoves.Count} good moves.");
        }
        return boardNode.BestMoves.ElementAt(_random.Next(boardNode.BestMoves.Count));
    }
}