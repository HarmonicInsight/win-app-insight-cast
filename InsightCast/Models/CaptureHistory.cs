using System;
using System.Collections.Generic;
using System.Linq;

namespace InsightCast.Models
{
    /// <summary>
    /// アノテーションの履歴管理（Undo/Redo）
    /// </summary>
    public class CaptureHistory
    {
        private readonly List<List<CaptureAnnotation>> _undoStack = new();
        private readonly List<List<CaptureAnnotation>> _redoStack = new();
        private const int MaxHistorySize = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 現在の状態を履歴に保存
        /// </summary>
        public void SaveState(IEnumerable<CaptureAnnotation> annotations)
        {
            var snapshot = annotations.Select(a => (CaptureAnnotation)a.Clone()).ToList();
            _undoStack.Add(snapshot);

            // 履歴サイズ制限
            while (_undoStack.Count > MaxHistorySize)
            {
                _undoStack.RemoveAt(0);
            }

            // 新しい操作をしたらRedoスタックをクリア
            _redoStack.Clear();
        }

        /// <summary>
        /// Undo実行：前の状態を返す
        /// </summary>
        public List<CaptureAnnotation>? Undo(IEnumerable<CaptureAnnotation> currentState)
        {
            if (!CanUndo) return null;

            // 現在の状態をRedoスタックに保存
            var current = currentState.Select(a => (CaptureAnnotation)a.Clone()).ToList();
            _redoStack.Add(current);

            // 前の状態を取得
            var previous = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            return previous.Select(a => (CaptureAnnotation)a.Clone()).ToList();
        }

        /// <summary>
        /// Redo実行：次の状態を返す
        /// </summary>
        public List<CaptureAnnotation>? Redo(IEnumerable<CaptureAnnotation> currentState)
        {
            if (!CanRedo) return null;

            // 現在の状態をUndoスタックに保存
            var current = currentState.Select(a => (CaptureAnnotation)a.Clone()).ToList();
            _undoStack.Add(current);

            // 次の状態を取得
            var next = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            return next.Select(a => (CaptureAnnotation)a.Clone()).ToList();
        }

        /// <summary>
        /// 履歴をクリア
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
