using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;
using System.Collections.Immutable;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 Bound IR 转换为 SSA IR。
/// 遍历 Bound 的结构化语句，生成扁平的 SsaBlock + SsaValue。
/// </summary>
sealed partial class SsaBuilder
{
    private void EmitStatements(ImmutableArray<BoundStmt> stmts)
    {
        foreach (var stmt in stmts)
        {
            // 语句粒度行号（JVM LineNumberTable 同粒度）：语句内表达式继承语句行；
            // 合成语句（EmptyStmt 等，Line=0 表示未知）保持上一行
            var stmtLine = stmt.Syntax?.Line ?? 0;
            if (stmtLine > 0)
                _currentLine = stmtLine;
            EmitStatement(stmt);
            if (_currentBlock.IsTerminated)
                break;
        }
    }

    private void EmitStatement(BoundStmt stmt)
    {
        switch (stmt)
        {
            case BoundVariableDeclaration decl:
                EmitVarDecl(decl.Variable, decl.Initializer);
                break;
            case BoundExprStatement exprStmt:
                EmitExpression(exprStmt.Expression);
                break;
            case BoundReturnStatement ret:
                EmitReturn(ret);
                break;
            case BoundIfStatement ifStmt:
                EmitIf(ifStmt);
                break;
            case BoundWhileStatement whileStmt:
                EmitWhile(whileStmt);
                break;
            case BoundForStatement forStmt:
                EmitFor(forStmt);
                break;
            case BoundUntilStatement untilStmt:
                EmitUntil(untilStmt);
                break;
            case BoundGotoStatement gs:
                EmitGoto(gs.Label);
                break;
            case BoundConditionalGotoStatement cgs:
                EmitCondGoto(cgs);
                break;
            case BoundLabelStatement ls:
                EmitLabel(ls.Label);
                break;
            case BoundKeyActStatement key:
                EmitKeyAction(key);
                break;
            case BoundStickActStatement stick:
                EmitStickAction(stick);
                break;
            case BoundFieldAssignStatement fa:
                EmitFieldAssign(fa);
                break;
            case BoundIndexAssignStatement ia:
                EmitIndexAssign(ia);
                break;
            case BoundNop:
                break;
            case BoundBlockStatement block:
                EmitStatements(block.Statements);
                break;
        }
    }

    private void EmitVarDecl(VariableSymbol variable, BoundExpr initializer)
    {
        var initVal = EmitExpression(initializer);

        if (variable is GlobalVariableSymbol)
        {
            var store = NewValue(SsaOp.StoreGlobal, variable.Type, initVal, aux: variable);
            AddInst(store);
            // 全局变量不进 SSA：后续引用走 LoadGlobal 读取全局存储的当前值
        }
        else
        {
            // 局部变量进 SSA：StoreLocal 维持运行时槽位一致性（参数/ EvalFrame 需要），
            // 同时把 RHS 值登记为该变量在当前块的 SSA 定义——后续读变量直接命中，
            // 合并点由 phi 自动合并（取代旧的 LoadLocal 重载）。
            var store = NewValue(SsaOp.StoreLocal, variable.Type, initVal, aux: variable);
            AddInst(store);
            _vars.WriteVariable(variable, initVal, _currentBlock);
        }
    }

    private void EmitReturn(BoundReturnStatement ret)
    {
        SsaValue? retVal = null;
        if (ret.Expression != null)
            retVal = EmitExpression(ret.Expression);

        var returnVal = NewValue(SsaOp.Return, ScriptType.Void, retVal);
        AddInst(returnVal);
        _currentBlock.IsReturn = true;
    }

    private void EmitIf(BoundIfStatement ifStmt)
    {
        // 死续区内整个 IF 不可达：不建任何块/边（死块若接了边，合并点 sealed 时
        // 会沿幽灵边生成 0 臂占位 φ）
        if (_inDeadSink) return;

        var cond = EmitExpression(ifStmt.Condition);

        var thenBlock = CreateBlock();
        var endBlock = CreateBlock();
        SsaBlock? elseBlock = ifStmt.ElseBody != null || !ifStmt.ElseIfs.IsDefaultOrEmpty
            ? CreateBlock() : null;

        // 当前块发出条件分支
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = thenBlock;
        _currentBlock.FalseSuccessor = elseBlock ?? endBlock;
        thenBlock.AddPredecessor(_currentBlock);
        _currentBlock.FalseSuccessor.AddPredecessor(_currentBlock);

        // Then 分支（单前驱，进入即封闭）
        SwitchToBlock(thenBlock, fromConditionalBranch: true);
        _vars.SealBlock(thenBlock);
        EmitStatements(ifStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock) && !_inDeadSink)
        {
            _currentBlock.JumpTarget = endBlock;
            endBlock.AddPredecessor(_currentBlock);
        }

        // ElseIfs + Else
        if (elseBlock != null)
        {
            SwitchToBlock(elseBlock, fromConditionalBranch: true);
            _vars.SealBlock(elseBlock);
            // 处理 ElseIfs 链
            if (!ifStmt.ElseIfs.IsDefaultOrEmpty)
            {
                foreach (var (elifCond, elifBody) in ifStmt.ElseIfs)
                {
                    var elifCondVal = EmitExpression(elifCond);
                    var elifThen = CreateBlock();
                    var elifEnd = CreateBlock(); // 临时，下一轮 else 或 end

                    _currentBlock.BranchCondition = elifCondVal;
                    elifCondVal.Uses++;
                    _currentBlock.TrueSuccessor = elifThen;
                    _currentBlock.FalseSuccessor = elifEnd;
                    elifThen.AddPredecessor(_currentBlock);
                    elifEnd.AddPredecessor(_currentBlock);

                    SwitchToBlock(elifThen, fromConditionalBranch: true);
                    _vars.SealBlock(elifThen);
                    EmitStatements(elifBody.Statements);
                    if (NeedsTerminator(_currentBlock) && !_inDeadSink)
                    {
                        _currentBlock.JumpTarget = endBlock;
                        endBlock.AddPredecessor(_currentBlock);
                    }

                    SwitchToBlock(elifEnd);
                    _vars.SealBlock(elifEnd);
                }
            }
            // Else body
            if (ifStmt.ElseBody != null)
            {
                EmitStatements(ifStmt.ElseBody.Statements);
            }
            if (NeedsTerminator(_currentBlock) && !_inDeadSink)
            {
                _currentBlock.JumpTarget = endBlock;
                endBlock.AddPredecessor(_currentBlock);
            }
        }

        // endBlock 是合并点：两臂体已全部发出，封闭后 SwitchToBlock 才能在其上读出正确 phi
        _vars.SealBlock(endBlock);
        SwitchToBlock(endBlock);
    }

    private void EmitWhile(BoundWhileStatement whileStmt)
    {
        if (_inDeadSink) return;

        var headerBlock = CreateBlock();
        var bodyBlock = CreateBlock();
        var endBlock = CreateBlock();

        // 注册 break/continue 标签映射
        _labelBlocks[whileStmt.BreakLabel] = endBlock;
        _labelBlocks[whileStmt.ContinueLabel] = headerBlock;

        // 当前块 → headerBlock
        _currentBlock.JumpTarget = headerBlock;
        headerBlock.AddPredecessor(_currentBlock);

        // headerBlock：判断条件（此时 headerBlock 尚未封闭——回边未连好；
        // 在 header 内读循环变量会触发占位 phi 插入，待回边连好后再封闭回填）
        SwitchToBlock(headerBlock);
        var cond = EmitExpression(whileStmt.Condition);
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = bodyBlock;
        _currentBlock.FalseSuccessor = endBlock;
        bodyBlock.AddPredecessor(_currentBlock);
        endBlock.AddPredecessor(_currentBlock);

        // bodyBlock（单前驱 header，进入即封闭）
        SwitchToBlock(bodyBlock, fromConditionalBranch: true);
        _vars.SealBlock(bodyBlock);
        EmitStatements(whileStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock) && !_inDeadSink)
        {
            _currentBlock.JumpTarget = headerBlock; // 回边
            headerBlock.AddPredecessor(_currentBlock);
        }

        // 回边已连好 → 封闭 headerBlock（回填 header 内的占位 phi，闭合循环变量）
        _vars.SealBlock(headerBlock);
        // endBlock（单前驱 header false，可封闭）
        _vars.SealBlock(endBlock);
        SwitchToBlock(endBlock);
    }

    private void EmitFor(BoundForStatement forStmt)
    {
        if (_inDeadSink) return;

        var headerBlock = CreateBlock();
        var endBlock = CreateBlock();

        _labelBlocks[forStmt.BreakLabel] = endBlock;

        if (forStmt.Kind2 == ForKind.Infinite)
        {
            var bodyBlock = CreateBlock();

            _labelBlocks[forStmt.ContinueLabel] = headerBlock;

            _currentBlock.JumpTarget = headerBlock;
            headerBlock.AddPredecessor(_currentBlock);

            // header: 无条件 true → body（未封闭，回边后封闭）
            SwitchToBlock(headerBlock);
            var trueVal = NewValue(SsaOp.ConstBool, ScriptType.Bool);
            trueVal.Const.SetBool(true);
            AddInst(trueVal);
            _currentBlock.BranchCondition = trueVal;
            trueVal.Uses++;
            _currentBlock.TrueSuccessor = bodyBlock;
            _currentBlock.FalseSuccessor = endBlock;
            bodyBlock.AddPredecessor(_currentBlock);
            endBlock.AddPredecessor(_currentBlock);

            // body（单前驱 header true，进入即封闭）
            SwitchToBlock(bodyBlock, fromConditionalBranch: true);
            _vars.SealBlock(bodyBlock);
            EmitStatements(forStmt.Body.Statements);

            if (NeedsTerminator(_currentBlock) && !_inDeadSink)
            {
                _currentBlock.JumpTarget = headerBlock;
                headerBlock.AddPredecessor(_currentBlock);
            }

            // 回边已连好 → 封闭 headerBlock、endBlock
            _vars.SealBlock(headerBlock);
            _vars.SealBlock(endBlock);
        }
        else
        {
            // For_Static / For_Full
            var variable = forStmt.Variable!;
            var upperBound = forStmt.UpperBound!;
            var bodyBlock = CreateBlock();
            var continueBlock = CreateBlock();
            var isGlobal = variable is GlobalVariableSymbol;
            var storeOp = isGlobal ? SsaOp.StoreGlobal : SsaOp.StoreLocal;

            // CONTINUE 跳到 continueBlock（step 发生的地方）
            _labelBlocks[forStmt.ContinueLabel] = continueBlock;

            // 初始化变量: $i = lower（局部进 SSA：登记定义；全局发 StoreGlobal）
            var initVal = EmitExpression(forStmt.LowerBound!);
            var store = NewValue(storeOp, ScriptType.Void, initVal, aux: variable);
            AddInst(store);
            if (!isGlobal)
                _vars.WriteVariable(variable, initVal, _currentBlock);

            // 计算上限值
            var upperVal = EmitExpression(upperBound);

            // 当前块 → header
            _currentBlock.JumpTarget = headerBlock;
            headerBlock.AddPredecessor(_currentBlock);

            // header: 读 $i（局部走 ReadVariable → header 内触发占位 phi；全局发 LoadGlobal）+ LeqInt → body / end
            SwitchToBlock(headerBlock);
            var varVal = isGlobal
                ? NewValue(SsaOp.LoadGlobal, ScriptType.Int, aux: variable)
                : _vars.ReadVariable(variable, ScriptType.Int, headerBlock);
            if (isGlobal) AddInst(varVal);
            var cond = NewValue(SsaOp.LeqInt, ScriptType.Bool, varVal, upperVal);
            AddInst(cond);
            _currentBlock.BranchCondition = cond;
            cond.Uses++;
            _currentBlock.TrueSuccessor = bodyBlock;
            _currentBlock.FalseSuccessor = endBlock;
            bodyBlock.AddPredecessor(_currentBlock);
            endBlock.AddPredecessor(_currentBlock);

            // body: 用户代码（单前驱 header true，进入即封闭）
            SwitchToBlock(bodyBlock, fromConditionalBranch: true);
            _vars.SealBlock(bodyBlock);
            EmitStatements(forStmt.Body.Statements);

            // body 落空 → postBodyCheck: 检查是否需要 increment
            //    如果当前值 == upper：已经是最后迭代的值，直接跳到 end（不 increment，保持 $i = upper）
            //    如果当前值 < upper：需要继续循环，跳到 continueBlock 执行 increment
            if (NeedsTerminator(_currentBlock) && !_inDeadSink)
            {
                var varValCheck = isGlobal
                    ? NewValue(SsaOp.LoadGlobal, ScriptType.Int, aux: variable)
                    : _vars.ReadVariable(variable, ScriptType.Int, _currentBlock);
                if (isGlobal) AddInst(varValCheck);
                var isLastIter = NewValue(SsaOp.EqInt, ScriptType.Bool, varValCheck, upperVal);
                AddInst(isLastIter);
                _currentBlock.BranchCondition = isLastIter;
                isLastIter.Uses++;
                _currentBlock.TrueSuccessor = endBlock;         // 最后一次 → 跳到 end（不 increment）
                _currentBlock.FalseSuccessor = continueBlock;   // 否则 → increment
                endBlock.AddPredecessor(_currentBlock);
                continueBlock.AddPredecessor(_currentBlock);
            }

            // continueBlock（单前驱 body false，进入即封闭）: increment $i = $i + 1, jump to header
            SwitchToBlock(continueBlock);
            _vars.SealBlock(continueBlock);
            var varValForInc = isGlobal
                ? NewValue(SsaOp.LoadGlobal, ScriptType.Int, aux: variable)
                : _vars.ReadVariable(variable, ScriptType.Int, continueBlock);
            if (isGlobal) AddInst(varValForInc);
            var oneVal = NewValue(SsaOp.ConstInt, ScriptType.Int);
            oneVal.Const.SetInt(1);
            AddInst(oneVal);
            var stepVal = NewValue(SsaOp.AddInt, ScriptType.Int, varValForInc, oneVal);
            AddInst(stepVal);
            var stepStore = NewValue(storeOp, ScriptType.Void, stepVal, aux: variable);
            AddInst(stepStore);
            if (!isGlobal)
                _vars.WriteVariable(variable, stepVal, continueBlock);

            _currentBlock.JumpTarget = headerBlock;
            headerBlock.AddPredecessor(_currentBlock);

            // 回边已连好 → 封闭 headerBlock（回填 $i 占位 phi：init←initVal, step←stepVal）
            _vars.SealBlock(headerBlock);
            _vars.SealBlock(endBlock);
        }

        SwitchToBlock(endBlock);
    }

    private void EmitUntil(BoundUntilStatement untilStmt)
    {
        if (_inDeadSink) return;

        var headerBlock = CreateBlock();
        var bodyBlock = CreateBlock();
        var endBlock = CreateBlock();

        _labelBlocks[untilStmt.BreakLabel] = endBlock;
        _labelBlocks[untilStmt.ContinueLabel] = headerBlock;

        _currentBlock.JumpTarget = headerBlock;
        headerBlock.AddPredecessor(_currentBlock);

        // headerBlock: 评估条件，true → break, false → body（未封闭，回边后封闭）
        SwitchToBlock(headerBlock);
        var cond = EmitExpression(untilStmt.Condition);
        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        _currentBlock.TrueSuccessor = endBlock;   // 条件为 true → 结束
        _currentBlock.FalseSuccessor = bodyBlock;  // 条件为 false → 继续循环
        endBlock.AddPredecessor(_currentBlock);
        bodyBlock.AddPredecessor(_currentBlock);

        // bodyBlock（单前驱 header false，进入即封闭） → 跳回 header
        SwitchToBlock(bodyBlock, fromConditionalBranch: true);
        _vars.SealBlock(bodyBlock);
        EmitStatements(untilStmt.Body.Statements);
        if (NeedsTerminator(_currentBlock) && !_inDeadSink)
        {
            _currentBlock.JumpTarget = headerBlock;
            headerBlock.AddPredecessor(_currentBlock);
        }

        // 回边已连好 → 封闭 headerBlock；endBlock（单前驱 header true）亦可封闭
        _vars.SealBlock(headerBlock);
        _vars.SealBlock(endBlock);
        SwitchToBlock(endBlock);
    }

    private void EmitGoto(BoundLabel label)
    {
        var target = GetOrCreateLabelBlock(label);
        // 死续区内再跳转：区块仍死，不接线（块保持无前驱，后续按死块收尾）
        if (_inDeadSink) return;
        _currentBlock.JumpTarget = target;
        target.AddPredecessor(_currentBlock);
        // 创建新块供后续（不可达）语句使用
        SwitchToBlock(CreateBlock());
        _inDeadSink = true;
    }

    private void EmitCondGoto(BoundConditionalGotoStatement cgs)
    {
        // 死续区内的条件跳转整体不可达：不建边（fallThrough 块无前驱会成幽灵）
        if (_inDeadSink) return;
        var cond = EmitExpression(cgs.Condition);
        var target = GetOrCreateLabelBlock(cgs.Label);
        var fallThrough = CreateBlock();

        _currentBlock.BranchCondition = cond;
        cond.Uses++;
        if (cgs.JumpIfTrue)
        {
            _currentBlock.TrueSuccessor = target;
            _currentBlock.FalseSuccessor = fallThrough;
        }
        else
        {
            _currentBlock.FalseSuccessor = target;
            _currentBlock.TrueSuccessor = fallThrough;
        }
        target.AddPredecessor(_currentBlock);
        fallThrough.AddPredecessor(_currentBlock);
        SwitchToBlock(fallThrough, fromConditionalBranch: true);
    }

    private void EmitLabel(BoundLabel label)
    {
        var labelBlock = GetOrCreateLabelBlock(label);
        // 如果当前块未终止，连接到 labelBlock（死续块不接线：其边是幽灵，标签块的
        // 真实入边来自指向它的 goto 本身）
        if (NeedsTerminator(_currentBlock) && !_inDeadSink)
        {
            _currentBlock.JumpTarget = labelBlock;
            labelBlock.AddPredecessor(_currentBlock);
        }
        SwitchToBlock(labelBlock);
    }

    private void EmitKeyAction(BoundKeyActStatement key)
    {
        if (key is BoundKeyPressStatement bps)
        {
            var dur = EmitExpression(bps.Duration);
            var v = NewValue(SsaOp.KeyPress, ScriptType.Void, dur, aux: new GamePadKeySymbol(bps.Act));
            AddInst(v);
        }
        else
        {
            var v = NewValue(SsaOp.KeyAction, ScriptType.Void, aux: new GamePadKeySymbol(key.Act));
            v.Const.SetBool(key.Up);
            AddInst(v);
        }
    }

    private void EmitStickAction(BoundStickActStatement stick)
    {
        if (stick is BoundStickPressStatement bps)
        {
            var dur = EmitExpression(bps.Duration);
            var v = NewValue(SsaOp.StickPress, ScriptType.Void, dur, aux: new GamePadKeySymbol(bps.Act));
            v.Const.SetInt(((int)bps.X << 16) | ((int)bps.Y << 8) | bps.Degree);
            AddInst(v);
        }
        else
        {
            var v = NewValue(SsaOp.StickAction, ScriptType.Void, aux: new GamePadKeySymbol(stick.Act));
            v.Const.SetInt(((int)stick.X << 16) | ((int)stick.Y << 8) | stick.Degree);
            AddInst(v);
        }
    }

    private void EmitFieldAssign(BoundFieldAssignStatement fa)
    {
        var target = EmitExpression(fa.Target);
        var value = EmitExpression(fa.Value);
        var v = NewValue(SsaOp.StoreField, fa.Field.FieldType, target, value, aux: fa.Field);
        AddInst(v);
    }

    private void EmitIndexAssign(BoundIndexAssignStatement ia)
    {
        // 检测 container 是字段访问且字段为数组类型 → StoreFieldIndex
        if (ia.Container is BoundFieldAccessExpression fa && fa.Field.FieldType is ArrayType)
        {
            var target = EmitExpression(fa.Target);
            var idx = EmitExpression(ia.Index);
            var val = EmitExpression(ia.Value);
            var elemType = ((ArrayType)fa.Field.FieldType).ElementType;
            var sv = NewValue(SsaOp.StoreFieldIndex, elemType, target, idx, aux: fa.Field);
            sv.ExtraArgs = new List<SsaValue> { val };
            AddExtraUses(sv.ExtraArgs);
            AddInst(sv);
            return;
        }

        var container = EmitExpression(ia.Container);
        var index = EmitExpression(ia.Index);
        var value = EmitExpression(ia.Value);
        var v = NewValue(SsaOp.StoreIndex, ScriptType.Void, container, index);
        v.ExtraArgs = new List<SsaValue> { value };
        AddExtraUses(v.ExtraArgs);
        AddInst(v);
    }

    // ============ 表达式发射 ============

    private SsaValue EmitExpression(BoundExpr expr)
    {
        // 仅对 BoundLiteralExpression 使用常量快速路径
        // BoundVariableExpression 也设置了 ConstantValue（初始值），但变量可变，必须走 Load 路径
        return expr switch
        {
            BoundLiteralExpression lit => EmitConst(lit.Type, lit.ConstantValue),
            BoundVariableExpression var => EmitVariable(var),
            BoundBinaryExpression bin => EmitBinary(bin),
            BoundUnaryExpression un => EmitUnary(un),
            BoundConversionExpression conv => EmitConversion(conv),
            BoundCallExpression call => EmitCall(call),
            BoundIndexVariableExpression idx => EmitIndex(idx),
            BoundSliceExpression slice => EmitSlice(slice),
            BoundIndexDeclxpression decl => EmitArrayInit(decl),
            BoundStructInitExpression si => EmitStructInit(si),
            BoundFieldAccessExpression fa => EmitFieldAccess(fa),
            BoundRuntimeValueExpression rv => EmitRuntimeValue(rv),
            BoundImageLabelExpression il => EmitImageLabel(il),
            BoundErrorExpression => NewValue(SsaOp.Nop, ScriptType.Int),
            _ => throw new InvalidOperationException($"未知表达式: {expr.Kind}")
        };
    }

    private SsaValue EmitConst(ScriptType type, object? value)
    {
        var op = type switch
        {
            _ when type.Equals(ScriptType.Bool) => SsaOp.ConstBool,
            _ when type.Equals(ScriptType.Byte) => SsaOp.ConstByte,
            _ when type.Equals(ScriptType.Int) => SsaOp.ConstInt,
            _ when type.Equals(ScriptType.UInt) => SsaOp.ConstUInt,
            _ when type.Equals(ScriptType.UInt64) => SsaOp.ConstUInt64,
            _ when type.Equals(ScriptType.Double) => SsaOp.ConstDouble,
            _ when type.Equals(ScriptType.String) => SsaOp.ConstString,
            _ when type.Equals(ScriptType.Ptr) => SsaOp.ConstPtr,
            _ => SsaOp.ConstInt
        };
        var v = NewValue(op, type);
        if (value != null)
            SetConstPayload(v, value);
        if (op == SsaOp.ConstString && value == null)
            v.ConstString = "";
        AddInst(v);  // 常量必须加入指令列表以便 Evaluator 执行并写入 _valueCache
        return v;
    }
}