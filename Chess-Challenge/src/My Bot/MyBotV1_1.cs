using ChessChallenge.API;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

public class MyBotV1_1 : IChessBot
{
    private readonly Dictionary<ulong, Tuple<double, bool, HashSet<Move>>> _searchMap = new();
    private readonly Dictionary<ulong, Dictionary<Move, ulong>> _historyMap = new();
    private readonly Queue<IEnumerable<Move>> _searchQueue = new();
    private const int _maxSearchDepth = 3;
    private const int _searchTimeout = 5000;
    private readonly Random _random = new();

    private bool OverrideNodeMoveIfNeeded(ref double bestValue, double newValue, bool thinkToWhite)
    {
        var changeMove = (thinkToWhite && bestValue < newValue)
            || (!thinkToWhite && bestValue > newValue);

        if (changeMove)
        {
            bestValue = newValue;
        }
        return changeMove;
    }

    private void AddBestValueToSearchMap(Board board, double newValue, bool isGameStillGoing, HashSet<Move> movesWithValue)
    {
        if (!_searchMap.TryAdd(board.ZobristKey, Tuple.Create(newValue, isGameStillGoing, movesWithValue)))
        {
            var (currentNodeValue, currentNodeIsGameStillGoing, currentNodeMoves) = _searchMap[board.ZobristKey];
            if (OverrideNodeMoveIfNeeded(ref currentNodeValue, newValue, board.IsWhiteToMove))
            {
                _searchMap[board.ZobristKey] = Tuple.Create(currentNodeValue, isGameStillGoing, movesWithValue);
            }
            else if (currentNodeValue == newValue)
            {
                _searchMap[board.ZobristKey] = Tuple.Create(currentNodeValue, currentNodeIsGameStillGoing || isGameStillGoing, currentNodeMoves.Union(movesWithValue).ToHashSet());
            }
        }
    }

    // Returns if the game still going.
    private bool EvaluateBoard(Board board, int elapsedTime, out double boardValue, int movesSoFar)
    {
        if (board.IsDraw())
        {
            boardValue = 0;
            return false;
        }

        var searchTreeHeightPenality = (1000 - movesSoFar) / 1000.0;
        if (board.IsInCheckmate())
        {
            var winner = board.IsWhiteToMove ? -1 : 1;
            boardValue = winner * (255 + 1000 * searchTreeHeightPenality);
            return false;
        }

        var pieceLists = board.GetAllPieceLists();
        var boardPiecesValue = pieceLists.Sum(pl => (pl.IsWhitePieceList ? 1 : -1) * pl.Count * (((int)pl.TypeOfPieceInList)) * (((int)pl.TypeOfPieceInList)));
        boardValue = boardPiecesValue * (2 - elapsedTime / 10000.0) * searchTreeHeightPenality;
        return true;
    }

    public Move Think(Board board, Timer timer)
    {
        _searchQueue.Enqueue(new List<Move>());
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
            if (_searchMap.TryGetValue(board.ZobristKey, out var val) && !val.Item2)
            {
                foreach (var realizedMove in currentBoardSetUp.Reverse())
                {
                    board.UndoMove(realizedMove);
                }
                continue;
            }

            try
            {
                var nodeBestValue = (double)(board.IsWhiteToMove ? int.MinValue : int.MaxValue);
                HashSet<Move> nodeBestMoves = new();
                Dictionary<Move, ulong> nodeTransitions = new();
                foreach (var previewMove in board.GetLegalMoves())
                {
                    board.MakeMove(previewMove);

                    var nodeValue = default(double);
                    if (_searchMap.TryGetValue(board.ZobristKey, out var searchMapValue))
                    {
                        nodeValue = searchMapValue.Item1;
                        if (searchMapValue.Item2 && currentBoardSetUp.Count() < _maxSearchDepth)
                        {
                            _searchQueue.Enqueue(currentBoardSetUp.Append(previewMove));
                        }
                    }
                    else if (EvaluateBoard(board, timer.MillisecondsElapsedThisTurn, out nodeValue, board.GameMoveHistory.Length + currentBoardSetUp.Count()) && currentBoardSetUp.Count() < _maxSearchDepth)
                    {
                        _searchQueue.Enqueue(currentBoardSetUp.Append(previewMove));
                    }

                    if (OverrideNodeMoveIfNeeded(ref nodeBestValue, nodeValue, !board.IsWhiteToMove))
                    {
                        nodeBestMoves = new HashSet<Move>
                        {
                            previewMove
                        };
                    }
                    else if (nodeBestValue == nodeValue)
                    {
                        nodeBestMoves.Add(previewMove);
                    }
                    nodeTransitions.Add(previewMove, board.ZobristKey);

                    board.UndoMove(previewMove);
                }
                _historyMap[board.ZobristKey] = nodeTransitions;
                AddBestValueToSearchMap(board, nodeBestValue, true, nodeBestMoves);
            }
            finally
            {
                foreach (var realizedMove in currentBoardSetUp.Reverse())
                {
                    var (childNodeValue, gameStillGoing, _) = _searchMap[board.ZobristKey];
                    board.UndoMove(realizedMove);
                    if (_historyMap.ContainsKey(board.ZobristKey) && !_searchMap.TryAdd(board.ZobristKey, Tuple.Create(childNodeValue, gameStillGoing, new HashSet<Move> { realizedMove })))
                    {
                        var (parentNodeValue, _, parentNodeMoves) = _searchMap[board.ZobristKey];
                        if (OverrideNodeMoveIfNeeded(ref parentNodeValue, childNodeValue, board.IsWhiteToMove))
                        {
                            _searchMap[board.ZobristKey] = Tuple.Create(parentNodeValue, gameStillGoing, new HashSet<Move>
                            {
                                realizedMove
                            });
                        }
                        else if (childNodeValue == parentNodeValue)
                        {
                            parentNodeMoves.Add(realizedMove);
                        }
                        else
                        {
                            parentNodeMoves.Remove(realizedMove);
                        }

                        if (parentNodeMoves.Count == 0)
                        {
                            var allTransitions = _historyMap[board.ZobristKey];
                            var internalNodeBestValue = (double)(board.IsWhiteToMove ? int.MinValue : int.MaxValue);
                            var internalNodeIsGameStillGoing = true;
                            foreach (var (move, childZobristKey) in allTransitions)
                            {
                                if (!_searchMap.ContainsKey(childZobristKey))
                                {
                                    continue;
                                }
                                var (currentChildNodeValue, isGameStillGoingForChild, _) = _searchMap[childZobristKey];
                                if (OverrideNodeMoveIfNeeded(ref internalNodeBestValue, currentChildNodeValue, board.IsWhiteToMove))
                                {
                                    internalNodeIsGameStillGoing = isGameStillGoingForChild;
                                    parentNodeMoves = new HashSet<Move>
                                    {
                                        move
                                    };
                                }
                                else if (internalNodeBestValue == currentChildNodeValue)
                                {
                                    internalNodeIsGameStillGoing = true;
                                    parentNodeMoves.Add(move);
                                }
                            }
                            _searchMap[board.ZobristKey] = Tuple.Create(internalNodeBestValue, internalNodeIsGameStillGoing, parentNodeMoves);
                        }
                    }
                }
            }
        }
        var allMoves = _searchMap[board.ZobristKey].Item3;
        return allMoves.ElementAt(_random.Next(allMoves.Count));
    }
}