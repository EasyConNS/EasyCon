// namespace LinearScanRegisterAllocation
namespace EasyCon.Script.Binding;

// 寄存器信息
public class RegisterInfo
{
    public int Index { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public bool IsFree { get; set; }
    public int LastUse { get; set; }

    public RegisterInfo(int index)
    {
        Index = index;
        IsFree = true;
        LastUse = -1;
    }
}

// 线性扫描寄存器分配器
class LinearScanRegisterAllocator
{
    private readonly List<RegisterInfo> registers;
    private readonly List<BoundVariableDeclaration> assignments;
    private readonly Dictionary<string, int> variableLastUse;
    private readonly Dictionary<string, int> registerAssignment;
    private readonly Dictionary<string, int> variableFirstUse;

    public LinearScanRegisterAllocator()
    {
        // 初始化8个寄存器 (0-7)
        registers = new List<RegisterInfo>();
        for (int i = 0; i < 8; i++)
        {
            registers.Add(new RegisterInfo(i));
        }

        assignments = new List<BoundVariableDeclaration>();
        variableLastUse = new Dictionary<string, int>();
        registerAssignment = new Dictionary<string, int>();
        variableFirstUse = new Dictionary<string, int>();
    }

    // 添加赋值语句
    public void AddAssignment(BoundStmt stmt)
    {
        if (stmt is BoundVariableDeclaration assignment)
            assignments.Add(assignment);
        // else if (stmt)
    }

    // 计算变量的最后使用位置
    private void ComputeVariableLastUse()
    {
        for (int i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];

            // 更新赋值变量的最后使用位置
            variableLastUse[assignment.Variable.Name] = i;

            // 更新表达式中变量的最后使用位置
            var referencedVars = assignment.Initializer.GetReferencedVariables();
            foreach (var varName in referencedVars)
            {
                variableLastUse[varName] = i;

                // 记录首次使用位置
                if (!variableFirstUse.ContainsKey(varName))
                {
                    variableFirstUse[varName] = i;
                }
            }

            // 记录赋值变量的首次使用位置
            if (!variableFirstUse.ContainsKey(assignment.Variable.Name))
            {
                variableFirstUse[assignment.Variable.Name] = i;
            }
        }
    }

    // 查找空闲寄存器
    private int FindFreeRegister()
    {
        for (int i = 0; i < registers.Count; i++)
        {
            if (registers[i].IsFree)
            {
                return i;
            }
        }
        return -1; // 没有空闲寄存器
    }

    // 查找最后使用最远的寄存器（用于溢出）
    private int FindRegisterToSpill(string newVariable, int currentPosition)
    {
        int farthestUse = -1;
        int registerToSpill = -1;

        for (int i = 0; i < registers.Count; i++)
        {
            if (!registers[i].IsFree)
            {
                var varName = registers[i].VariableName;
                if (variableLastUse.TryGetValue(varName, out int lastUse))
                {
                    if (lastUse > currentPosition && lastUse > farthestUse)
                    {
                        farthestUse = lastUse;
                        registerToSpill = i;
                    }
                }
                else
                {
                    // 如果没有记录最后使用，立即溢出这个寄存器
                    return i;
                }
            }
        }

        // 如果所有寄存器中的变量都在当前之后使用，选择最后使用最远的
        return registerToSpill != -1 ? registerToSpill : 0;
    }

    // 分配寄存器给变量
    private int AllocateRegisterForVariable(string variableName, int currentPosition)
    {
        // 检查变量是否已经分配了寄存器
        if (registerAssignment.TryGetValue(variableName, out int existingRegister))
        {
            // 更新寄存器的最后使用时间
            var reg = registers[existingRegister];
            if (variableLastUse.TryGetValue(variableName, out int lastUse))
            {
                reg.LastUse = lastUse;
            }
            return existingRegister;
        }

        // 尝试查找空闲寄存器
        int freeRegister = FindFreeRegister();
        if (freeRegister != -1)
        {
            // 分配空闲寄存器
            registers[freeRegister].IsFree = false;
            registers[freeRegister].VariableName = variableName;
            if (variableLastUse.TryGetValue(variableName, out int lastUse))
            {
                registers[freeRegister].LastUse = lastUse;
            }
            registerAssignment[variableName] = freeRegister;
            return freeRegister;
        }

        // 没有空闲寄存器，需要溢出
        int registerToSpill = FindRegisterToSpill(variableName, currentPosition);
        var spilledReg = registers[registerToSpill];
        string spilledVariable = spilledReg.VariableName;

        Console.WriteLine($"Spilling variable '{spilledVariable}' from register R{registerToSpill} for variable '{variableName}'");

        // 移除溢出变量的寄存器分配
        registerAssignment.Remove(spilledVariable);

        // 分配寄存器给新变量
        spilledReg.VariableName = variableName;
        if (variableLastUse.TryGetValue(variableName, out int newLastUse))
        {
            spilledReg.LastUse = newLastUse;
        }
        registerAssignment[variableName] = registerToSpill;

        return registerToSpill;
    }

    // 释放不再使用的寄存器
    private void FreeUnusedRegisters(int currentPosition)
    {
        foreach (var reg in registers)
        {
            if (!reg.IsFree && reg.LastUse < currentPosition)
            {
                Console.WriteLine($"Freeing register R{reg.Index} (variable '{reg.VariableName}')");
                reg.IsFree = true;
                registerAssignment.Remove(reg.VariableName);
                reg.VariableName = string.Empty;
            }
        }
    }

    // 执行寄存器分配
    public void AllocateRegisters()
    {
        Console.WriteLine("Starting Linear Scan Register Allocation...");
        Console.WriteLine($"Total assignments: {assignments.Count}");
        Console.WriteLine();

        // 计算变量的最后使用位置
        ComputeVariableLastUse();

        Console.WriteLine("Variable last use positions:");
        foreach (var kvp in variableLastUse.OrderBy(k => k.Value))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }
        Console.WriteLine();

        // 线性扫描每个赋值语句
        for (int i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            Console.WriteLine($"Processing assignment {i}: {assignment.Variable} = ...");

            // 释放当前不再使用的寄存器
            FreeUnusedRegisters(i);

            // 为表达式中引用的变量分配寄存器
            var referencedVars = assignment.Initializer.GetReferencedVariables();
            foreach (var varName in referencedVars)
            {
                int regIndex = AllocateRegisterForVariable(varName, i);
                Console.WriteLine($"  Variable '{varName}' in expression -> R{regIndex}");
            }

            // 为被赋值的变量分配寄存器
            int assignReg = AllocateRegisterForVariable(assignment.Variable.Name, i);
            Console.WriteLine($"  Variable '{assignment.Variable}' being assigned -> R{assignReg}");

            Console.WriteLine();
        }

        // 输出最终分配结果
        Console.WriteLine("Final Register Allocation:");
        foreach (var reg in registers)
        {
            if (!reg.IsFree)
            {
                Console.WriteLine($"  R{reg.Index}: {reg.VariableName}");
            }
            else
            {
                Console.WriteLine($"  R{reg.Index}: [FREE]");
            }
        }
    }
}