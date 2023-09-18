using ChessChallenge.API;
using System;
using System.Collections.Generic;
using System.Linq;

public class MyBotV1_3 : IChessBot
{
    private readonly Dictionary<ulong, BoardNode> _searchMap = new();
    private const int _maxSearchDepth = 7;
    private const int _searchTimeout = 5000;
    private const double _deltaThreshhold = 15;
    private readonly Random _random = new();

    private struct BoardNode
    {
        public readonly bool IsWhiteToMove;
        private readonly double _stateValue;
        private double _value;
        public bool IsSearchDone = false;
        public HashSet<Move> BestMoves = new();
        public readonly Dictionary<Move, ulong> Transitions = new();

        public double Value => _value == int.MinValue || _value == int.MaxValue ? _stateValue : _value;

        public BoardNode(Board board, double stateValue, bool isSearchDone)
        {
            IsWhiteToMove = board.IsWhiteToMove;
            _stateValue = stateValue;
            IsSearchDone = isSearchDone;
            _value = IsWhiteToMove ? int.MinValue : int.MaxValue;
        }

        public bool IsQuiescenceChild(BoardNode childNode, double threshhold)
        {
            return Math.Pow(_stateValue - childNode.Value, 2) > threshhold;
        }

        public void UpdateValue(BoardNode node, Move move)
        {
            var newValue = node.Value;
            if (_value == newValue)
            {
                BestMoves.Add(move);
                IsSearchDone = IsSearchDone && node.IsSearchDone;
                return;
            }

            if ((IsWhiteToMove && _value < newValue)
            || (!IsWhiteToMove && _value > newValue))
            {
                _value = newValue;
                BestMoves = new() { move };
                IsSearchDone = node.IsSearchDone;
                return;
            }
            else
            {
                BestMoves.Remove(move);
                IsSearchDone = IsSearchDone && BestMoves.Count > 0;
                return;
            }
        }

        public void Reset()
        {
            _value = IsWhiteToMove ? int.MinValue : int.MaxValue;
            BestMoves = new();
            IsSearchDone = false;
        }

        public override string ToString()
        {
            return $"BoardNode({(IsWhiteToMove ? "White" : "Black")}, [{Value} ({_stateValue})], [{string.Join(", ", BestMoves)}], {IsSearchDone}, {Transitions.Count})";
        }
    }

    private static BoardNode EvaluateBoard(Board board, int movesSoFar)
    {
        var lastPlayerSignal = board.IsWhiteToMove ? -1 : 1;
        var boardPiecesValue = board.GetAllPieceLists().Sum(pl => (pl.IsWhitePieceList ? 1 : -1) * pl.Count * Math.Pow((int)pl.TypeOfPieceInList, 2));
        var searchTreeHeightPenality = (1000 - movesSoFar) / 1000.0;
        double boardValue;
        var isGameEnded = true;
        if (board.IsDraw())
        {
            boardValue = board.IsInsufficientMaterial() ? 0 : -0.75 * Math.Sign(Math.Sign(boardPiecesValue) + lastPlayerSignal);
        }
        else if (board.IsInCheckmate())
        {
            boardValue = lastPlayerSignal * (500 + 1000 * searchTreeHeightPenality);
        }
        else
        {
            boardValue = boardPiecesValue * searchTreeHeightPenality;
            boardValue *= 1 + ((board.IsInCheck() ? Math.Sign(boardValue) * lastPlayerSignal : 0) / 10.0);
            isGameEnded = false;
        }
        return new(board, boardValue, isGameEnded);
    }

    private BoardNode EvaluateBoardNode(Board board, Timer timer, IEnumerable<Move> boardSetUp, bool expandQuiescenceNode)
    {
        if (!_searchMap.TryGetValue(board.ZobristKey, out var parentBoardNode))
        {
            parentBoardNode = EvaluateBoard(board, board.GameMoveHistory.Length + boardSetUp.Count());
        }

        if (!expandQuiescenceNode || timer.MillisecondsElapsedThisTurn < _searchTimeout)
        {
            var previewMoves = board.GetLegalMoves(true).Union(board.GetLegalMoves());
            foreach (var previewMove in previewMoves)
            {
                board.MakeMove(previewMove);

                if (!_searchMap.TryGetValue(board.ZobristKey, out var childNode))
                {
                    childNode = EvaluateBoard(board, board.GameMoveHistory.Length + boardSetUp.Count() + 1);
                    _searchMap.Add(board.ZobristKey, childNode);
                }

                if (parentBoardNode.IsQuiescenceChild(childNode, _deltaThreshhold) && expandQuiescenceNode && !childNode.IsSearchDone && boardSetUp.Count() == _maxSearchDepth && childNode.Transitions.Count == 0)
                {
                    childNode = EvaluateBoardNode(board, timer, boardSetUp.Append(previewMove), false);
                }
                parentBoardNode.Transitions.Add(previewMove, board.ZobristKey);
                parentBoardNode.UpdateValue(childNode, previewMove);

                board.UndoMove(previewMove);
            }
        }

        _searchMap[board.ZobristKey] = parentBoardNode;
        return parentBoardNode;
    }

    private bool ExploreBoard(ulong currentZobristKey, Board board, Timer timer, IEnumerable<Move> currentBoardSetUp, double alpha = int.MaxValue, double beta = int.MinValue)
    {
        if (!_searchMap.TryGetValue(currentZobristKey, out var currentBoardNode) || currentBoardNode.Transitions.Count == 0)
        {
            foreach (var move in currentBoardSetUp)
            {
                board.MakeMove(move);
            }
            currentBoardNode = EvaluateBoardNode(board, timer, currentBoardSetUp, true);
            foreach (var move in currentBoardSetUp.Reverse())
            {
                board.UndoMove(move);
            }
        }

        if ((currentBoardNode.IsWhiteToMove && currentBoardNode.Value > alpha) || (!currentBoardNode.IsWhiteToMove && currentBoardNode.Value < beta))
        {
            return false;
        }

        if (currentBoardSetUp.Count() < _maxSearchDepth && timer.MillisecondsElapsedThisTurn < _searchTimeout)
        {
            currentBoardNode.Reset();
            alpha = int.MaxValue;
            beta = int.MinValue;
            foreach (var (previewMove, childZobristKey) in currentBoardNode.Transitions)
            {
                if (ExploreBoard(childZobristKey, board, timer, currentBoardSetUp.Append(previewMove), alpha, beta) && _searchMap.TryGetValue(childZobristKey, out var childBoardNode))
                {
                    currentBoardNode.UpdateValue(childBoardNode, previewMove);
                    alpha = Math.Min(alpha, currentBoardNode.Value);
                    beta = Math.Max(beta, currentBoardNode.Value);
                }
            }
            _searchMap[currentZobristKey] = currentBoardNode;
        }
        return true;
    }

    public Move Think(Board board, Timer timer)
    {
        if (!_searchMap.TryGetValue(board.ZobristKey, out var curr) || !curr.IsSearchDone)
        {
            ExploreBoard(board.ZobristKey, board, timer, new List<Move>());
        }
       
        var allMoves = _searchMap[board.ZobristKey].BestMoves;
        return allMoves.ElementAt(_random.Next(allMoves.Count));
    }
}