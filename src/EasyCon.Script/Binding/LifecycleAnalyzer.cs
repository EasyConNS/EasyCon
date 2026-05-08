using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding;

/// <summary>
/// 分析变量的生命周期，实现变量slot的优化分配
/// 将生命周期不重叠的变量分配到相同的slot中，以减少内存使用
/// </summary>
internal static class LifecycleAnalyzer
{
    /// <summary>
    /// 为函数体中的局部变量分配slot，优化内存使用
    /// 为函数体中所有未分配 slot 的局部变量分配索引。
    /// 参数 slot 已在 BindFuncDeclaration 中分配（0..N-1），
    /// 局部变量从 N 开始分配，使用生命周期分析优化slot分配。
    /// </summary>
    public static void AllocateLocalSlots(FunctionSymbol function, BoundBlockStatement body)
    {
        // 获取所有局部变量声明
        var localVariables = CollectLocalVariables(body);

        // 如果没有局部变量，直接返回
        if (localVariables.Count == 0)
        {
            function.LocalSlotCount = function.Parameters.Length;
            return;
        }

        // 分析变量的生命周期
        var lifecycles = AnalyzeLifecycles(body, localVariables);

        // 根据生命周期分配slot
        var slotMap = AllocateSlots(lifecycles);

        // 应用slot分配
        ApplySlotAllocation(localVariables, slotMap, function.Parameters.Length);

        // 更新函数的slot计数
        function.LocalSlotCount = function.Parameters.Length + slotMap.Values.Distinct().Count();
    }

    /// <summary>
    /// 收集函数体中的所有局部变量声明
    /// </summary>
    private static List<LocalVariableSymbol> CollectLocalVariables(BoundBlockStatement body)
    {
        var variables = new List<LocalVariableSymbol>();
        CollectVariablesRecursive(body, variables);
        return variables;
    }

    /// <summary>
    /// 递归收集变量声明
    /// </summary>
    private static void CollectVariablesRecursive(BoundBlockStatement body, List<LocalVariableSymbol> variables)
    {
        foreach (var stmt in body.Statements)
        {
            switch (stmt)
            {
                case BoundVariableDeclaration vd when vd.Variable is LocalVariableSymbol local:
                    variables.Add(local);
                    break;
                case BoundBlockStatement inner:
                    CollectVariablesRecursive(inner, variables);
                    break;
            }
        }
    }

    /// <summary>
    /// 分析变量的生命周期
    /// 返回每个变量的生命周期范围（起始和结束的位置索引）
    /// </summary>
    private static Dictionary<LocalVariableSymbol, VariableLifecycle> AnalyzeLifecycles(BoundBlockStatement body, List<LocalVariableSymbol> variables)
    {
        var lifecycles = new Dictionary<LocalVariableSymbol, VariableLifecycle>();
        var statementIndex = 0;
        var variablePositions = new Dictionary<LocalVariableSymbol, List<int>>();

        // 初始化所有变量的位置列表
        foreach (var variable in variables)
        {
            variablePositions[variable] = new List<int>();
        }

        // 遍历所有语句，记录变量出现的位置
        AnalyzeStatementReferences(body, variablePositions, ref statementIndex);

        // 根据变量出现的位置计算生命周期
        foreach (var kvp in variablePositions)
        {
            var variable = kvp.Key;
            var positions = kvp.Value;

            if (positions.Count > 0)
            {
                // 生命周期从第一次使用开始到最后一次使用结束
                lifecycles[variable] = new VariableLifecycle(positions.Min(), positions.Max());
            }
            else
            {
                // 如果变量从未被引用，给它一个默认的生命周期
                lifecycles[variable] = new VariableLifecycle(0, 0);
            }
        }

        return lifecycles;
    }

    /// <summary>
    /// 递归分析语句中变量的引用位置
    /// </summary>
    private static void AnalyzeStatementReferences(BoundStmt stmt, Dictionary<LocalVariableSymbol, List<int>> variablePositions, ref int statementIndex)
    {
        // 记录当前语句的位置
        var currentPosition = statementIndex++;

        switch (stmt)
        {
            case BoundVariableDeclaration vd:
                // 变量声明语句
                if (vd.Variable is LocalVariableSymbol local)
                {
                    // 添加声明位置
                    if (variablePositions.ContainsKey(local))
                        variablePositions[local].Add(currentPosition);

                    // 分析初始化表达式中的变量引用
                    AnalyzeExpressionReferences(vd.Initializer, variablePositions, currentPosition);
                }
                break;

            case BoundExprStatement es:
                AnalyzeExpressionReferences(es.Expression, variablePositions, currentPosition);
                break;

            case BoundBlockStatement block:
                foreach (var innerStmt in block.Statements)
                {
                    AnalyzeStatementReferences(innerStmt, variablePositions, ref statementIndex);
                }
                break;

            case BoundIfStatement ifs:
                AnalyzeExpressionReferences(ifs.Condition, variablePositions, currentPosition);
                AnalyzeStatementReferences(ifs.Body, variablePositions, ref statementIndex);
                foreach (var elseif in ifs.ElseIfs)
                {
                    AnalyzeExpressionReferences(elseif.Condition, variablePositions, currentPosition);
                    AnalyzeStatementReferences(elseif.Body, variablePositions, ref statementIndex);
                }
                if (ifs.ElseBody != null)
                    AnalyzeStatementReferences(ifs.ElseBody, variablePositions, ref statementIndex);
                break;

            case BoundWhileStatement ws:
                AnalyzeExpressionReferences(ws.Condition, variablePositions, currentPosition);
                AnalyzeStatementReferences(ws.Body, variablePositions, ref statementIndex);
                break;

            case BoundReturnStatement rs:
                if (rs.Expression != null)
                    AnalyzeExpressionReferences(rs.Expression, variablePositions, currentPosition);
                break;

            case BoundFieldAssignStatement fas:
                AnalyzeExpressionReferences(fas.Target, variablePositions, currentPosition);
                AnalyzeExpressionReferences(fas.Value, variablePositions, currentPosition);
                break;

            case BoundIndexAssignStatement ias:
                AnalyzeExpressionReferences(ias.Container, variablePositions, currentPosition);
                AnalyzeExpressionReferences(ias.Index, variablePositions, currentPosition);
                AnalyzeExpressionReferences(ias.Value, variablePositions, currentPosition);
                break;

                // 其他语句类型可以根据需要添加
        }
    }

    /// <summary>
    /// 分析表达式中变量的引用
    /// </summary>
    private static void AnalyzeExpressionReferences(BoundExpr expr, Dictionary<LocalVariableSymbol, List<int>> variablePositions, int position)
    {
        switch (expr)
        {
            case BoundVariableExpression ve when ve.Variable is LocalVariableSymbol local:
                if (variablePositions.ContainsKey(local))
                    variablePositions[local].Add(position);
                break;

            case BoundUnaryExpression ue:
                AnalyzeExpressionReferences(ue.Operand, variablePositions, position);
                break;

            case BoundBinaryExpression be:
                AnalyzeExpressionReferences(be.Left, variablePositions, position);
                AnalyzeExpressionReferences(be.Right, variablePositions, position);
                break;

            case BoundCallExpression ce:
                foreach (var arg in ce.Arguments)
                    AnalyzeExpressionReferences(arg, variablePositions, position);
                break;

            case BoundConversionExpression ce:
                AnalyzeExpressionReferences(ce.Expression, variablePositions, position);
                break;

            case BoundIndexVariableExpression ive:
                AnalyzeExpressionReferences(ive.BaseExpression, variablePositions, position);
                AnalyzeExpressionReferences(ive.Index, variablePositions, position);
                break;

            case BoundFieldAccessExpression fae:
                AnalyzeExpressionReferences(fae.Target, variablePositions, position);
                break;

            case BoundFieldIndexAccessExpression fiae:
                AnalyzeExpressionReferences(fiae.Target, variablePositions, position);
                AnalyzeExpressionReferences(fiae.Index, variablePositions, position);
                break;
        }
    }

    /// <summary>
    /// 根据变量生命周期分配slot
    /// 使用贪心算法：将生命周期不重叠的变量分配到相同slot
    /// </summary>
    private static Dictionary<LocalVariableSymbol, int> AllocateSlots(Dictionary<LocalVariableSymbol, VariableLifecycle> lifecycles)
    {
        var slotMap = new Dictionary<LocalVariableSymbol, int>();
        var slots = new List<List<VariableLifecycle>>(); // 每个slot中已分配变量的生命周期

        // 按照生命周期起始位置排序变量
        var sortedVariables = lifecycles.OrderBy(kvp => kvp.Value.Start).ToList();

        foreach (var kvp in sortedVariables)
        {
            var variable = kvp.Key;
            var lifecycle = kvp.Value;

            // 寻找第一个可以容纳该变量的slot（生命周期不重叠）
            int slotIndex = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                // 检查是否与slot中所有变量的生命周期都不重叠
                bool overlaps = false;
                foreach (var existingLifecycle in slots[i])
                {
                    if (lifecycle.OverlapsWith(existingLifecycle))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    slotIndex = i;
                    break;
                }
            }

            // 如果没有找到合适的slot，创建新的slot
            if (slotIndex == -1)
            {
                slotIndex = slots.Count;
                slots.Add(new List<VariableLifecycle>());
            }

            // 分配slot
            slotMap[variable] = slotIndex;
            slots[slotIndex].Add(lifecycle);
        }

        return slotMap;
    }

    /// <summary>
    /// 应用slot分配结果
    /// </summary>
    private static void ApplySlotAllocation(List<LocalVariableSymbol> variables, Dictionary<LocalVariableSymbol, int> slotMap, int parameterCount)
    {
        // 参数slot已经分配（0..parameterCount-1），局部变量从parameterCount开始
        var slotOffsets = new Dictionary<int, int>(); // slotIndex -> actualSlotIndex
        var nextSlot = parameterCount;

        // 为每个使用的slot分配实际的slot索引
        var usedSlots = slotMap.Values.Distinct().OrderBy(x => x);
        foreach (var slot in usedSlots)
        {
            slotOffsets[slot] = nextSlot++;
        }

        // 应用分配
        foreach (var variable in variables)
        {
            if (slotMap.TryGetValue(variable, out var slot))
            {
                variable.SlotIndex = slotOffsets[slot];
            }
            else
            {
                // 默认分配
                variable.SlotIndex = nextSlot++;
            }
        }
    }
}

/// <summary>
/// 变量生命周期信息
/// </summary>
internal class VariableLifecycle
{
    public int Start { get; }
    public int End { get; }

    public VariableLifecycle(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// 检查两个生命周期是否重叠
    /// </summary>
    public bool OverlapsWith(VariableLifecycle other)
    {
        // 两个区间重叠的条件：start1 <= end2 && start2 <= end1
        return Start <= other.End && other.Start <= End;
    }
}