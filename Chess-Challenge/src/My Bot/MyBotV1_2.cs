using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBotV1_2 : IChessBot
{
    private readonly Dictionary<ulong, BoardNode> _searchMap = new();
    private readonly Queue<IEnumerable<Move>> _searchQueue = new();
    private const int _maxSearchDepth = 3;
    private const int _searchTimeout = 5000;
    private readonly Random _random = new();

    private struct BoardNode
    {
        private readonly bool _isWhiteToMove;

        public double Value;
        public bool IsSearchDone = false;
        public readonly HashSet<Move> BestMoves = new();
        public readonly Dictionary<Move, ulong> Transitions = new();

        private BoardNode(bool isWhiteToMove, double value, bool isSearchDone, HashSet<Move> bestMoves, Dictionary<Move, ulong> transitions)
        {
            _isWhiteToMove = isWhiteToMove;
            Value = value;
            BestMoves = bestMoves;
            IsSearchDone = isSearchDone;
            Transitions = transitions;
        }

        public BoardNode(Board board)
        {
            _isWhiteToMove = board.IsWhiteToMove;
            Value = _isWhiteToMove ? int.MinValue : int.MaxValue;
        }

        public BoardNode(double value, bool isWhiteToMove, bool isSearchDone)
        {
            _isWhiteToMove = isWhiteToMove;
            Value = value;
            IsSearchDone = isSearchDone;
        }

        public void UpdateValue(double newValue, bool isSearchDone, Move move)
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
            else
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
        {
            if (_searchMap.TryGetValue(board.ZobristKey, out var boardNode))
            {
                boardNode.IsSearchDone = false;
            }
        }
        while (_searchQueue.Count > 0)
        {
            if (timer.MillisecondsElapsedThisTurn > _searchTimeout)
            {
                _searchQueue.Clear();
                break;
            }
            var currentBoardSetUp = _searchQueue.Dequeue();
            foreach (var realizedMove in currentBoardSetUp)
            {
                board.MakeMove(realizedMove);
            }
            if (_searchMap.TryGetValue(board.ZobristKey, out var val) && val.IsSearchDone)
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
                parentBoardNode.ResetValue();
                foreach (var previewMove in board.GetLegalMoves())
                {
                    board.MakeMove(previewMove);
                    if (!_searchMap.TryGetValue(board.ZobristKey, out var childNode))
                    {
                        var isEnded = EvaluateBoard(board, timer.MillisecondsElapsedThisTurn, out var nodeValue, board.GameMoveHistory.Length + currentBoardSetUp.Count());
                        childNode = new(nodeValue, board.IsWhiteToMove, isEnded);
                        _searchMap.Add(board.ZobristKey, childNode);
                    }

                    if (!childNode.IsSearchDone && currentBoardSetUp.Count() < _maxSearchDepth)
                    {
                        _searchQueue.Enqueue(currentBoardSetUp.Append(previewMove));
                    }
                    parentBoardNode.UpdateValue(childNode.Value, childNode.IsSearchDone, previewMove);
                    board.UndoMove(previewMove);
                }
            }
            finally
            {
                foreach (var realizedMove in currentBoardSetUp.Reverse())
                {
                    var childBoardNode = _searchMap[board.ZobristKey];
                    board.UndoMove(realizedMove);
                    var parentBoardNode = _searchMap[board.ZobristKey];
                    parentBoardNode.UpdateValue(childBoardNode.Value, childBoardNode.IsSearchDone, realizedMove);
                    if (parentBoardNode.BestMoves.Count == 0)
                    {
                        parentBoardNode.ResetValue();
                        foreach (var (move, zobristKey) in parentBoardNode.Transitions)
                        {
                            var auxChild = _searchMap[zobristKey];
                            parentBoardNode.UpdateValue(auxChild.Value, auxChild.IsSearchDone, move);
                        }
                    }
                }
            }
        }
        var allMoves = _searchMap[board.ZobristKey].BestMoves;
        return allMoves.ElementAt(_random.Next(allMoves.Count));
    }
}