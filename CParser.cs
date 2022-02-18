using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

//Copyright Bill Collis 2014

namespace SystemDesigner
{
    public class CParser : Parser
    {
        frmFastColoredTextBox parsetree;
        public CParser()
        {
            toknIndex = -1;
            Parserstate = ParserState.Run; //a new parser defaults to run mode not step mode
            ParserResult = ParserResult.Ok;
            RunDelay = 0;
            Tokens = new List<Token>();
            //Console.WriteLine("new C Parser");
            parsetree = null;
            //parsetree = new frmFastColoredTextBox();
            //parsetree.Show();
        }
        //http://stackoverflow.com/questions/11425985/autoresetevent-in-c-sharp
        
        #region parser structure
        public override void Parse(BackgroundWorker bgw) //defaults to run mode
        {
            interruptpending = "";
            IRQList = new List<string>(); //not used yet - will ultimately replace single interruptpending name

            backgroundWorkerMe = bgw;
            ClockTicks = 0;
            getNextTokn();              //read first tokn into currTokn
            ParseProgram();             //this parses only certain commands - those that occur before main()
            while (CurrentToken.Type != TokenType.tEOF && !backgroundWorkerMe.CancellationPending)
            {
                Breaktype breaktype;
                parseStatement(out breaktype);
            }
        }
        public void ParseProgram()
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseProgram");
            while ((CurrentToken.Type == TokenType.tInclude
                || CurrentToken.Type == TokenType.tStruct
                || CurrentToken.Type == TokenType.tDefine
                || CurrentToken.Type == TokenType.tCommand
                || CurrentToken.Type == TokenType.tSemicolon
                || CurrentToken.Type == TokenType.tCR
                || CurrentToken.Type == TokenType.tLF
                || CurrentToken.Type == TokenType.tConst
                || CurrentToken.Type == TokenType.tISR
                || CurrentToken.Type == TokenType.tFuncDecl
                || CurrentToken.Type == TokenType.tFuncProt
                || CurrentToken.Type == TokenType.tType) && !backgroundWorkerMe.CancellationPending)
            {
                switch (CurrentToken.Type)
                {
                    case TokenType.tISR:
                        parseISRDecl();
                        break;
                    case TokenType.tDefine:
                        parseDefine();
                        getNextTokn();
                        break;
                    case TokenType.tStruct:
                        parseStructDefn();
                        //getNextTokn();
                        break;
                    case TokenType.tInclude:
                        parseInclude();
                        break;
                    case TokenType.tConst:
                        parseConstant();
                        break;
                    case TokenType.tCommand:
                        parseCommand();
                        break;
                    case TokenType.tSemicolon:
                        getNextTokn();
                        break;
                    case TokenType.tCR:
                        getNextTokn();
                        break;
                    case TokenType.tLF:
                        getNextTokn();
                        break;
                    case TokenType.tType:  //global var decl, funcProt fucntDecl OR main               
                        if (toknIndex >= Tokens.Count)
                            error(CurrentToken, " unexpected end tto tthe program");
                        //nexttoken will be tIdent ot tFuncIdent or tPointer 
                        if (CurrentToken.Value == "unsigned") //shouldnt ever get here as lexer handles unsigned types
                        {
                            getNextTokn();
                        }
                        if (Tokens[toknIndex + 1].Type == TokenType.tVarIdent)
                        {
                            dynamic value;
                            VarType varType;
                            string identname;
                            ParseDeclaration("", out identname, out value, out varType);
                            //getNextTokn();
                        }
                        else if (Tokens[toknIndex + 1].Type == TokenType.tPointer)
                        {
                            dynamic value;
                            VarType varType;
                            string identname;
                            varPointerDeclaration(out identname, out value, out varType);
                            //getNextTokn();
                        }
                        else if (Tokens[toknIndex + 1].Value == "main") //if  void/int main(void) or void/int main()- currently ignoring if type is other than int/void
                        {
                            if (!(CurrentToken.Value == "void" || CurrentToken.Value == "int" || CurrentToken.Value == "int16_t"))
                                error(CurrentToken, " expected 'int' or 'void' but found  " + CurrentToken.Value);
                            getNextTokn();
                            matchThenGetNextTokn(TokenType.tFuncDecl);//main
                            matchThenGetNextTokn(TokenType.tOpenBracket);
                            if (!(CurrentToken.Value == "void" || CurrentToken.Type == TokenType.tCloseBracket))
                                error(CurrentToken, " expected 'void' or ')' but found  " + CurrentToken.Value);
                            if (CurrentToken.Value == "void")
                                matchThenGetNextTokn(TokenType.tType);//void
                            matchThenGetNextTokn(TokenType.tCloseBracket);
                            ScopeName = "main";
                            ScopeLevel++;
                            break; //out of while and then parse statements
                        }
                        else if (Tokens[toknIndex + 1].Type == TokenType.tFuncProt) //could be FuncProt, FunctDecl or int main(void)
                        {
                            parseFuncProt();
                        }
                        else if (Tokens[toknIndex + 1].Type == TokenType.tFuncDecl) //could be FuncProt, FunctDecl or int main(void)
                        {
                            parseFuncDecl();
                        }
                        else
                        {
                            error(Tokens[toknIndex + 1], " was not expecting this, was expecting a Function name");
                            getNextTokn();
                        }
                        break;
                }
            }
        }
        void parseCommand()
        {
            if (CurrentToken.Value == "ignoredelays") //this is processed elsewhere 
            {
                IgnoreDelays = true;
            }
            if (CurrentToken.Value == "noerrorbeep")
                Bugs.ErrorBeep = false;
            getNextTokn();
        }
        void parseStructDefn()
        {
            StructClassDefinition structDefn = new StructClassDefinition();
            if (parsetree != null) parsetree.SafeAddtext("-ParseStruct");
            getNextTokn(); //gp past the keyword "struct"
            if (!(CurrentToken.Type == TokenType.tVarIdent))
                error(CurrentToken, " expected a name for tthe struct");
            structDefn.Name = CurrentToken.Value;
            if (StructsClasses.StructClassDefnExists(structDefn.Name))
                error(CurrentToken, "a struct or class of name " + structDefn.Name + " already exists");
            getNextTokn();

            matchThenGetNextTokn(TokenType.tOpenBrace);
            if (CurrentToken.Type == TokenType.tCR)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tLF)
                getNextTokn();

            while (CurrentToken.Type == TokenType.tType) //e.g. int x, byte, b, char colour[4], ...
            {
                int arrcount = 0;
                if (Tokens[toknIndex + 1].Type == TokenType.tVarIdent)
                {
                    string t = CurrentToken.Value;
                    getNextTokn();
                    string n = CurrentToken.Value;
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tOpenSquareBracket)
                    {
                        getNextTokn();
                        arrcount = Convert.ToInt16(CurrentToken.Value);
                        getNextTokn(); // ']'
                        getNextTokn();
                        for (int i = 0; i < arrcount; i++)
                        {
                            structDefn.AddProperty(new Property(t,n+"["+i+"]"));
                        }
                    }
                    else
                        structDefn.AddProperty(new Property(t,n)); //not an array
                    if (CurrentToken.Type == TokenType.tSemicolon)
                        getNextTokn();
                    if (CurrentToken.Type == TokenType.tCR)
                        getNextTokn();
                    if (CurrentToken.Type == TokenType.tLF)
                        getNextTokn();
                }
            }
            //add to the StructsClasses dict
            StructsClasses.addStruct(structDefn);
            matchThenGetNextTokn(TokenType.tCloseBrace);
            while (CurrentToken.Type == TokenType.tVarIdent) //declare the structs in mem
            {
                foreach (Property p in structDefn.Properties) //declare the variable
                {
                    string s = CurrentToken.Value + "." + p.Name;
                    if (varNameUsedAlready(s))
                    {
                        error(CurrentToken, " tthe name  " + s + " has been used already");
                    }
                    else
                    {
                        Memory.declVariable(s, p.Type, MemArea.Data, ScopeLevel, ScopeName); //all structs go into Data area
                    }
                }
                getNextTokn();
                if (CurrentToken.Type == TokenType.tComma)
                    getNextTokn();
            }
            matchThenGetNextTokn(TokenType.tSemicolon);
        }
        void parseInclude()
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseInclude");
            //do nothing
            getNextTokn();
        }
        void parseStatement()
        {
            Breaktype b;
            parseStatement(out b);
        }
        void parseStatement(out Breaktype breaktype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-ParseStatement");
            dynamic value = null;
            VarType vartype = VarType.none;
            parseStatement(out value, out vartype, out breaktype);
        }
        void parseStatement(out dynamic value, out VarType vartype, out Breaktype breaktype)   //STATEMENT
        {
            if (parsetree != null) parsetree.SafeAddtext("--ParseStatement");
            //Console.WriteLine("parseStatement entered: " + CurrentToken.ToString());
            breaktype = Breaktype.None;
            value = null;
            vartype = VarType.none;
            if (backgroundWorkerMe.CancellationPending)
            {
                //Console.WriteLine("Parse() -  Cancellation pending");
                return;
            }

            if (interruptpending != "")
            {
                if (Functions.FunctionDefinitionExists(interruptpending))
                {
                    int returnToken = toknIndex-1;
                    string intrvector = interruptpending;
                    foreach (MicrocontrollerInterrupt intr in Micro.Interrupts)//reset I Flag    (but only if edge or change???)
                        if (intr.ISR == interruptpending)
                            intr.ClearFlag();
                    interruptpending = ""; //reset so it doesn't get called multiple times
                    parseISRCall(intrvector);
                    value = null; //not interested in any return values
                    vartype = VarType.none;
                    toknIndex = returnToken;
                    getNextTokn();
                }
            }

            switch (CurrentToken.Type)
            {
                case TokenType.tLibCall: //function calls that are not found in the functions table will also go to parseLebcall - thi sis a bit redundant
                    parseLibCall(out value, out vartype);
                    break;
                case TokenType.tVarIdent: //could be a var or a struct/class name,
                    if (StructsClasses.StructClassDefnExists(CurrentToken.Value) && Tokens[toknIndex+1].Type == TokenType.tVarIdent) //if it is a struct or class name, is it followed by another varident (its a decl)
                    {
                        //its initialization of a struct or class 
                        StructClassDefinition structDefn = StructsClasses.GetStructClass(CurrentToken.Value);//get the struct
                        while (CurrentToken.Type == TokenType.tVarIdent) //declare the structs in mem
                        {
                            foreach (Property p in structDefn.Properties) //declare the variable
                            {
                                string s = CurrentToken.Value + "." + p.Name;
                                if (varNameUsedAlready(s))
                                {
                                    error(CurrentToken, " tthe name  " + s + " has been used already");
                                }
                                else
                                {
                                    Memory.declVariable(s, p.Type, MemArea.Data, ScopeLevel, ScopeName); //structs go into data area
                                }
                            }
                            getNextTokn();
                            if (CurrentToken.Type == TokenType.tComma)
                                getNextTokn();
                        }
                        matchThenGetNextTokn(TokenType.tSemicolon);
                    }
                    else
                        ParseAssignment(out value, out vartype);
                    break;
                case TokenType.tRegAddress:
                    ParseAssignment(out value, out vartype);
                    break;
                case TokenType.tDereferenceOp:
                    ParseAssignment(out value, out vartype);
                    break;
                case TokenType.tType:    //VarDecl  e.g. uint8_t x; etc or funcProt or funcDecl
                    if (CurrentToken.Value == "unsigned")
                    {
                        getNextTokn(); //to get the type - now need to make that unsigned
                    }
                    if (Tokens[toknIndex + 1].Type == TokenType.tVarIdent)
                    {
                        string identname;
                        ParseDeclaration("",out identname, out value, out vartype);
                    }
                    else if (Tokens[toknIndex + 1].Type == TokenType.tPointer)
                    {
                        string identname;
                        varPointerDeclaration(out identname, out value, out vartype);
                    }
                    else if (Tokens[toknIndex + 1].Type == TokenType.tFuncDecl)
                        parseFuncDecl();
                    else
                    {
                        getNextTokn();
                        error(CurrentToken, " was expecting a variable or function name");
                    }
                    break;
                case TokenType.tOpenBrace: //parse till matching closing brace
                    getNextTokn(); //skip open brace
                    while (CurrentToken.Type != TokenType.tCloseBrace && !backgroundWorkerMe.CancellationPending && breaktype == Breaktype.None)//here a check of cancellation 
                        parseStatement(out value, out vartype, out breaktype);
                    if (breaktype != Breaktype.None)
                        return;
                    matchCurrentTokn(TokenType.tCloseBrace);
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tCR)
                        getNextTokn();
                    break;
                case TokenType.tReturn:
                    breaktype = Breaktype.Return;
                    break;
                case TokenType.tBreak:
                    breaktype = Breaktype.Break;
                    break;
                case TokenType.tContinue:
                    breaktype = Breaktype.Continue;
                    break;
                case TokenType.tGoto:
                    breaktype = Breaktype.Goto;
                    break;
                case TokenType.tConst: //const definition e.g. const int x = 3;
                    parseConstant();
                    break;
                case TokenType.tDefine: //these have already been added to the SymbolsTable or the Constants Table
                    parseDefine();
                    break;
                case TokenType.tInclude: //ignore any of these
                    break;
                case TokenType.tDefineIdent:  //unpack this into its tokens
                    Factor(out value, out vartype);
                    break;
                case TokenType.tWhile:
                    ParseWhile();
                    break;
                case TokenType.tFor:
                    ParseFor();
                    break;
                case TokenType.tSwitch:
                    ParseSwitch(out breaktype);
                    break;
                case TokenType.tIf:
                    ParseIf(out breaktype);
                    break;
                case TokenType.tDo:
                    parseDo();
                    break;
                case TokenType.tLF:
                    getNextTokn();
                    break;
                case TokenType.tCR:
                    getNextTokn();
                    if (parsetree != null) parsetree.SafeCleartext();
                    break;
                case TokenType.tSemicolon: //ignore any of these for the moment
                    getNextTokn();
                    break;
                case TokenType.tCommentError:
                    error(CurrentToken, "tthe comment has a problem");
                    return;
                case TokenType.tEOF: //dont do anything with this here
                    return;
                case TokenType.tUDR: //0.7.2 for serial I/O
                    UDR(out value, out vartype);
                    return;
                default:
                    Token t = CurrentToken;
                    bool result = recursiveDescentParser(out value, out vartype);
                    if (!result)
                        error(t, " could not parse ");
                    break;
            }
        }
        void parseDefine() //will store the define in either constant table or symbol table depending on what it is
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseDefine");
            //e.g. #define PI 3.14159
            getNextTokn();
            //the next token shoul dbe a tDefineIdent or a tConstIdent - the lexer has already worked this out 0.5.1
            if (!(CurrentToken.Type == TokenType.tDefineIdent || CurrentToken.Type == TokenType.tConstIdent))
                error(CurrentToken, " expected a define or constant name here");
            string name = CurrentToken.Value;
            if (varNameUsedAlready(CurrentToken.Value))
                error(CurrentToken, " tthe name  " + CurrentToken.Value + " has been used already");
            getNextTokn(); //the value to associate with the define

            if (CurrentToken.Type == TokenType.tStringOfTokens) //this means the parser was passed a string of parseable tokens
            {
                Symbolstable.Micro = Micro;
                Symbolstable.AddSymbol(name, CurrentToken.Value);
                //now relace all the occurences of the symbol name in the list of tokens
                replaceSymbol(name);

                return;
            }
            else if (CurrentToken.Type == TokenType.tIntConst || CurrentToken.Type == TokenType.tFloatConst || CurrentToken.Type == TokenType.tStringConst) //e.g. 5 or 3.14 or "hello"
            {
                dynamic value; // try and convert it to a specific type
                VarType vartype; //the correct type that we want associated with the value
                GetAValue(CurrentToken, out value, out vartype);
                Constantstable.AddConstant(name, vartype, value);
            }
            else
            {
                error(CurrentToken, " got  " + CurrentToken.Value + " and expected a number or string)");
            }

        }
        void parseConstant()
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseConst");
            // e.g. const uint8_t width = 239;
            getNextTokn();
            matchCurrentTokn(TokenType.tType);
            string constType = CurrentToken.Value;
            getNextTokn();
            matchCurrentTokn(TokenType.tConstIdent);
            string constName = CurrentToken.Value;
            if (varNameUsedAlready(constName))
                error(CurrentToken, " tthe name " + constName + " has been used already");
            getNextTokn();
            matchCurrentTokn(TokenType.tAssign);
            getNextTokn();

            dynamic value;
            VarType vartype;            
            GetAValue(CurrentToken, out value, out vartype);
            vartype = getVarType(constType); //override the returned vartype with the assigned one 
            Constantstable.AddConstant(constName, vartype, value);
            getNextTokn();
        }
        bool boolexpression()
        {
            // have to deal with situation where calculations, constants etc where there is no relational operator in the boolexpression     e.g. #define PB_sw2_IsLow()   ~PIND & (1<<6)
            //read the tokens when get to the ')' return the true/false based upon the
            //bool val = false;
            // the first tokn of the boolexpr is in currtokn

            dynamic lhs;
            VarType vartype;
            recursiveDescentParser(out lhs, out vartype);
            return (lhs > 0); //check for non numeric (strings and bools)

            //Console.WriteLine("boolexpression:lhs {0}", lhs);
            // if (CurrentToken.Type == TokenType.tCloseBracket) //reached the end of bool expression so no reloperator
            // {
            //     if (lhs.GetType() is bool)
            //         return lhs;
            //     else if (lhs.GetType() is string)
            //         return true; // do something proper here
            //     else if (lhs > 0) //check for non numeric (strings and bools)
            //         return true;
            //     else
            //         return false;
            // }
            // else if (isRelOperator(CurrentToken))
            // {
            //     Token relop_tokn = CurrentToken; //keep the relOp token for testing below
            //     getNextTokn();  //eat the the relop token
            //     dynamic rhs;
            //     lowestPrecedence(out rhs, out vartype);
            //     //Console.WriteLine("boolexpression:rhs {0}", rhs);
            //     //check for consistency between types?
            //     if (relop_tokn.Type == TokenType.tEqual) //  ==
            //     {
            //         if (lhs == rhs) val = true;
            //     }
            //     else if (relop_tokn.Type == TokenType.tNotEqual) //  !=
            //     {
            //         if (lhs != rhs) val = true;
            //     }
            //     else if (relop_tokn.Type == TokenType.tLessThan) //  <
            //     {
            //         if (lhs < rhs) val = true;
            //     }
            //     else if (relop_tokn.Type == TokenType.tGreaterThan) //  >
            //     {
            //         if (lhs > rhs) val = true;
            //     }
            //     else if (relop_tokn.Type == TokenType.tLessEqual) //  <=
            //     {
            //         if (lhs <= rhs) val = true;
            //     }
            //     else if (relop_tokn.Type == TokenType.tGreaterEqual) //  >=
            //     {
            //         if (lhs >= rhs) val = true;
            //     }
            // }
            // else
            // {
            //     error(CurrentToken, " exected a relational operator such as:  ==  !=  <  >  <=  >=  got a  " + CurrentToken.Value );
            //     return false; 
            // }

            //// matchCurrentTokn(ToknType.tCloseBracket); //make sure there is a closing brace but do not move currTokn to next tokn (as we need to color this line)
            // //Console.WriteLine("boolexpression:result {0}", val.ToString());
            // return val;
        }
        #endregion

        #region libcalls
        void parseLibCall(out dynamic value, out VarType vartype)
        {
            value = 0;
            vartype = VarType.none;
            if (CurrentToken.Value.Contains("glcd_"))
            {
                parseGLCDCall();
                return;
            }
            else if (CurrentToken.Value.Contains("lcd_"))
            {
                parseLCDCall();
                return;
            }
            else
            {
                switch (CurrentToken.Value)
                {
                    case "printf": //http://www.sparetimelabs.com/tinyprintf/tinyprintf.php maybe write a function sometime for printf, but will prob need stdargs as well
                        getNextTokn();
                        matchThenGetNextTokn(TokenType.tOpenBracket);
                        while (CurrentToken.Type != TokenType.tCloseBracket)
                        {
                            getNextTokn(); //ignore
                        }
                        matchThenGetNextTokn(TokenType.tCloseBracket);
                        break;
                    case "_BV":
                        getNextTokn();
                        matchThenGetNextTokn(TokenType.tOpenBracket);
                        recursiveDescentParser(out value, out vartype);
                        matchThenGetNextTokn(TokenType.tCloseBracket);
                        value = 1 << value;
                        break;
                    case "_delay_ms":
                        if (Parserstate == ParserState.Step || IgnoreDelays) //in step mode or if ignioredelays comand is set the delays are not shown and not executed 
                        {
                            while (Tokens[toknIndex].Type != TokenType.tCR) //skip thru to end of the the line ignoring any delay value and not highlighting the delay line
                                toknIndex++;
                            CurrentToken = Tokens[toknIndex]; 
                            getNextTokn(); //highlight the line
                        }
                        else //in the run mode the delays are shown
                        {
                            getNextTokn();
                            matchThenGetNextTokn(TokenType.tOpenBracket);
                            recursiveDescentParser(out value, out vartype);
                            matchThenGetNextTokn(TokenType.tCloseBracket);
                            //if (!IgnoreDelays)
                            //{
                                delay(value * 1000);
                            //}
                        }
                        break;
                    case "_delay_us":
                        getNextTokn();
                        matchThenGetNextTokn(TokenType.tOpenBracket);
                        recursiveDescentParser(out value, out vartype);
                        matchThenGetNextTokn(TokenType.tCloseBracket);
                        delay(value);
                        break;
                    case "sei":
                        getNextTokn();
                        matchThenGetNextTokn(TokenType.tOpenBracket);
                        matchThenGetNextTokn(TokenType.tCloseBracket);
                        Registers.SetBit("SREG", 7);
                        break;
                    case "cli":
                        getNextTokn();
                        matchThenGetNextTokn(TokenType.tOpenBracket);
                        Registers.ClearBit("SREG", 7);
                        matchThenGetNextTokn(TokenType.tCloseBracket);
                        break;
                    case "ADCW": //read the analog value of the pin set by the MUX bits in ADMUX
                        byte channel = 0;
                        bool readOK = Registers.ReadRegister("ADMUX", out channel);
                        channel &= 0x1F; //get just the MUX bits - this is the ADC channel
                        //the value of any adc conversion is stored in the MicrocontrollerPin (it has already been converted to 0to1023 and has been checked that the DDR is set as an input    
                        //look through the Pins in the Microcontroller to find the pin the adc channel is linked to 
                        UInt16 adcval = Micro.GetADCValue(channel);
                        value = adcval;
                        //store the value into the two ADC registers - this assums ADLAR is 0 default right adjusted //0.7.1
                        Registers.WriteRegister("ADCL", (byte)(adcval & 0xff));
                        Registers.WriteRegister("ADCH", (byte)(adcval >> 8));
                        vartype = VarType.uint16_t;
                        getNextTokn();
                        break;
                    case "getadc":
                        break;
                    case "sound":
                        makesound();
                        break;
                    case "__malloc_heap_start":
                        value = _malloc_heap_start();
                        break;
                    case "__malloc_heap_end":
                        value = _malloc_heap_end();
                        break;
                    default:
                        error(CurrentToken, "no function '" + CurrentToken.Value + "' exists");
                        break;
                }
            }
        }
        void UDR(out dynamic value, out VarType vartype)//0.7.2 all processing of Regsiter UDR UDR0 come here for handling - tx only at this stage
        {
            value = null;
            vartype = VarType.none;
            int txbitdelay = 800;

            getNextTokn();
            if(CurrentToken.Type==TokenType.tAssign)
            {
                getNextTokn();
                bool result = recursiveDescentParser(out value, out vartype); //the value ot written to the TXD pin
                //write value into UDR0
                Registers.WriteRegister("UDR0", (byte)value);

                string n = ""; //the std USART has no number i.e. "" is the std  & "0" is the atmega328 - som enmay have more - wont worry about those
                if (Registers.RegisterExists("UCSR0A")) //e.g. atmega328
                    n = "0";
                //clear UCSRnA.UDREn / UCSRA.UDRE to say a tx is in proogress
                Registers.ClearBit("UCSR" + n + "A", 5); //UDRE is bit 5

                //allow the parser to cintinue while the data is sent in a new thread task
                Task.Factory.StartNew(() =>
                {          
                    //get number of bits to send UCSZn2:0 and UCSZ2:0
                    byte nbits = 5;//default 5 bits
                    bool bit;
                    byte udr;
                    bool sendbiteight;
                    bool parity = false; //no parity
                    bool oddparity = true;//odd is 1 / true
                    int paritycountones=0;
                    bool stopbit2 = false;
                    int totalbits = 2; //start bit and 1 stop bit
                    BitArray frame = new BitArray(13,true);//the max length of a frame is start,9data,parity, 2stop - make all true so that stop bits dont have to be made
                    //get UDR
                    Registers.ReadRegister("UDR"+n, out udr);
                    //number of bits of data to send
                    Registers.GetBit("UCSR" + n + "C", 1, out bit); //UCSZ00 - bit 1
                    if (bit)
                        nbits += 1;
                    Registers.GetBit("UCSR" + n + "C", 2, out bit); //UCSZ01 - bit 2
                    if (bit)
                        nbits += 2;
                    Registers.GetBit("UCSR" + n + "B", 2, out sendbiteight); //UCSZ02 - bit 2 - if an 8th bit is to be sent
                    if (sendbiteight)
                        nbits = 9;//nbits now = the number of bits to send 5,6,7,8,9
                    totalbits += nbits;
                    //send parity UPMn1:0
                    Registers.GetBit("UCSR" + n + "C", 5, out parity); //UCSRnC - bit 5 - 1 if parity selected
                    if (parity)
                    {
                        totalbits++; //yes parity
                        Registers.GetBit("UCSR" + n + "C", 4, out oddparity); //1 if odd parity selected
                    }
                    //see if 2 stop bits
                    Registers.GetBit("UCSR" + n + "C", 3, out stopbit2); //USBSn - bit 3
                    if (stopbit2) 
                        totalbits++;
                    
                    //now build the frame
                    frame[0] = false; //startbit
                    for (int i = 0; i < 8; i++)
                    {
                        frame[i+1] = (udr&(1<<i))!=0;
                        if (frame[i + 1]) //if true/1 
                            paritycountones++;
                        if (i>nbits)
                            break;
                    }
                    if (sendbiteight) //if it exists it will be in bit9 of the frame
                    {
                        Registers.GetBit("UCSR" + n + "B", 0, out bit); //the 8th bit is stored here
                        if (bit)
                        {
                            frame[9] = bit;
                            paritycountones++;
                        }
                    }
                    if (parity)
                    {
                            if (oddparity)
                            {
                                if (paritycountones % 2 == 0)
                                    frame[nbits + 1] = false;
                            }
                           else //even parity 
                            {
                                if (paritycountones % 2 == 1)
                                    frame[nbits + 1] = false;
                            }
                    }

                    //D.1 is always TXD, we initiAlly set it high if not so we get a clear view of the first stop bit
                    //Registers.GetBit("PORTD", 1, out bit);
                    //if (!bit)
                    //{
                    //    Registers.SetBit("PORTD", 1); //idle high
                    //    Thread.Sleep(txbitdelay * 3);
                    //}
                    int bitn=0;
                    while (bitn < totalbits)
                    {
                        if (frame[bitn]) // high/true/1
                            Registers.SetBit("PORTD", 1);
                        else
                            Registers.ClearBit("PORTD", 1);
                        Thread.Sleep(txbitdelay);  
                        //Registers.ShiftRight("UDR" + n);
                        bitn++;
                    }
                     //set UCSRnA.UDREn / UCSRA.UDRE to say a tx is finsihed
                    Registers.SetBit("UCSR" + n + "A", 5); //UDRE is bit 5 
                });
                //all done
                getNextTokn();
            }
            else
                error(CurrentToken, " expected '=' ");
        }
        void parseLCDCall()
        {
            dynamic value;
            VarType vartype;

            if (CharLCD_Viz == null)
            {
                error(CurrentToken, " there is no LCD connected");
                return;
            }
            switch (CurrentToken.Value)
            {
                case "lcd_init":
                    CharLCD_Viz.lcd_cls();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_cls":
                    CharLCD_Viz.lcd_cls();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_disp_char": //should be followed by a char c
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    if (matchCurrentTokn(TokenType.tCharConst))
                        CharLCD_Viz.lcd_disp_char(CurrentToken.Value[0]);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_home":
                    CharLCD_Viz.lcd_home();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_cursorXY": //intx, int y
                    dynamic x = 0;
                    dynamic y = 0;
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    recursiveDescentParser(out x, out vartype);
                    matchCurrentTokn(TokenType.tComma);
                    getNextTokn();
                    recursiveDescentParser(out y, out vartype);
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    CharLCD_Viz.lcd_cursorXY((int)x, (int)y);
                    break;
                case "lcd_line0":
                    CharLCD_Viz.lcd_line0();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_line1":
                    CharLCD_Viz.lcd_line1();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_line2":
                    CharLCD_Viz.lcd_line2();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_line3":
                    CharLCD_Viz.lcd_line3();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_cursorOn":
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_cursorOff":
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_disp_str": //string str
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tStringConst)
                        CharLCD_Viz.lcd_disp_str(CurrentToken.Value);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "lcd_disp_dec_uchar": //byte b
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    recursiveDescentParser(out value, out vartype); //get the value to be displayed
                    dynamic b;
                    coerceType(vartype, VarType.uint8_t, value, out b);
                    CharLCD_Viz.lcd_disp_dec_uchar((byte)b);
                    matchCurrentTokn(TokenType.tCloseBracket);
                    break;
                case "lcd_disp_dec_uint16": //int i
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    recursiveDescentParser(out value, out vartype); //get the value to be displayed
                    dynamic i;
                    coerceType(vartype, VarType.uint16_t, value, out i);
                    CharLCD_Viz.lcd_disp_dec_uint16 ((UInt16)i);
                    matchCurrentTokn(TokenType.tCloseBracket);
                    break;
                case "lcd_disp_dec_schar": //byte b
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    recursiveDescentParser(out value, out vartype); //get the value to be displayed
                    CharLCD_Viz.lcd_disp_dec_schar(value);
                    matchCurrentTokn(TokenType.tCloseBracket);
                    break;
                case "lcd_disp_bin_uchar": //byte b
                    break;
            }
        }
        void parseGLCDCall()
        {
            //dynamic value;
            VarType vartype;

            if (GLCD_Viz == null)
            {
                error(CurrentToken, " there is no LCD connected");
                return;
            }
            switch (CurrentToken.Value)
            {
                case "glcd_clear":
                    GLCD_Viz.glcd_clear();
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "glcd_putchar": //should be followed by a char c
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    if (matchCurrentTokn(TokenType.tCharConst))
                        GLCD_Viz.glcd_putchar(CurrentToken.Value[0]);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
                case "glcd_xy": //intx, int y
                    dynamic x = 0;
                    dynamic y = 0;
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    recursiveDescentParser(out x, out vartype);
                    matchCurrentTokn(TokenType.tComma);
                    getNextTokn();
                    recursiveDescentParser(out y, out vartype);
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    GLCD_Viz.glcd_xy((int)x, (int)y);
                    break;
                case "glcd_puts": //string str
                    getNextTokn();
                    matchCurrentTokn(TokenType.tOpenBracket);
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tStringConst)
                        GLCD_Viz.glcd_puts(CurrentToken.Value);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    break;
            }
        }
        void makesound()
        {
            //expect the following tokens pin,duration, pulses  "(porta.1, 500, 1000)" duration = number of pulses to send, pulses = number of clock pulses pin is low/high for
            // e.g. 1Khz 1 second when running at 1MHz 
            //translate these to windows beep(frequency, duration) as bascom sound command is quite different to windows beep command and relates to XTAL freq as well
            // windows beep Freq = xtal/2*pulses
            //Windows beep Duration = 1000 * pulses * period / xtal
            int freq; //ranges from 37 to 32767 Hz
            int duration;
            int pulsecounter;
            int numberofpulses;
            getNextTokn();
            matchThenGetNextTokn(TokenType.tOpenBracket); //e.g. PORTB
            matchThenGetNextTokn(TokenType.tRegAddress); //e.g. PORTB
            matchThenGetNextTokn(TokenType.tComma);
            matchThenGetNextTokn(TokenType.tRegBit); //e.g. B1
            matchThenGetNextTokn(TokenType.tComma);
            numberofpulses = getVarConst(CurrentToken);//duration - could be const or var - 
            getNextTokn();
            matchThenGetNextTokn(TokenType.tComma);
            pulsecounter = getVarConst(CurrentToken);//duration - could be const or var - 
            //Console.WriteLine("Bascom duration:" + numberofpulses + "  pulses:" + pulsecounter + "  crystal"+Crystal);            
            getNextTokn();
            matchThenGetNextTokn(TokenType.tCloseBracket);
            freq = Crystal / (2 * pulsecounter);
            duration = 1000 * numberofpulses * pulsecounter / Crystal;
            //Console.WriteLine("Windows beep freq:"+ freq + "  duration" + duration);

            Console.Beep(freq, duration);
        }
        void delay(int uSecs)
        {            
            //depending upon clock freq and delay amount increase the number of ticks and sleep
            int rundelaymem = RunDelay;
            RunDelay = 0; //we dont want the extra RunDelay while doing a user set delay we only want the actual delay 
            int hundredths = uSecs / 100000;
            //reportProgress(true); 
            while (!backgroundWorkerMe.CancellationPending && hundredths > 0)
            {
                reportProgress(false); //report progress but dont wait for step
                //Thread.Sleep(90); //0.1sec - fudge as the delay os slow
                new System.Threading.ManualResetEvent(false).WaitOne(100);  //http://stackoverflow.com/questions/5424667/alternatives-to-thread-sleep
                hundredths--;
                ClockTicks += Crystal / 10; // +0.1Sec
                uSecs -= 100000; // -0.1Sec
            }
            //Thread.Sleep(uSecs / 1000);
            new System.Threading.ManualResetEvent(false).WaitOne(uSecs / 1000);
            ClockTicks += Crystal / 1000000 * uSecs;
            RunDelay = rundelaymem;
        }
        /// <summary>
        /// return _heapstart
        /// </summary>
        int _malloc_heap_start()
        {
            dynamic value;
            VarType vartype;
            getNextTokn();
            if (CurrentToken.Type==TokenType.tAssign) //assign a new value to heap_start
            {
                getNextTokn();
                recursiveDescentParser(out value, out vartype);
                Memory._heapstart = value;
            }
            Constantstable.ChangeConstantValue("__malloc_heap_start", VarType.uint16_t, Memory._heapstart);
            Constantstable.ChangeConstantValue("__malloc_heap_end", VarType.uint16_t, Memory._heapend);
            return Memory._heapstart;
        }
        int _malloc_heap_end()
        {
            dynamic value;
            VarType vartype;
            getNextTokn();
            if (CurrentToken.Type == TokenType.tAssign) //assign a new value to heap_start
            {
                getNextTokn();
                recursiveDescentParser(out value, out vartype);
                Memory._heapend = value;
            }
            Constantstable.ChangeConstantValue("__malloc_heap_start", VarType.uint16_t, Memory._heapstart);
            Constantstable.ChangeConstantValue("__malloc_heap_end", VarType.uint16_t, Memory._heapend);
            return Memory._heapend;
        }
        #endregion

        #region handling vars
        /// <summary>
        /// a declaration means that we have a type and a var name
        /// will be in the form of these examples
        /// int a 
        /// int a,b,c,d
        /// int a=3
        /// int a=3,b=4,c=5
        /// int a=3,f,b=2,t  mixed mem areas
        /// int a[3]
        /// int a[3]={2,3,4}
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="identname"></param>
        /// <param name="value"></param>
        /// <param name="vartype"></param>
        /// <returns></returns>
        bool ParseDeclaration(string prefix, out string identname, out dynamic value, out VarType vartype) //starts parsing from the type
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseDeclaration");
            vartype = VarType.none;
            value = null;
            string varTypeStr = CurrentToken.Value; //the Type to declare
            //if last token was unsigned make this unsigned
            if (toknIndex > 1 && Tokens[toknIndex - 1].Value == "unsigned")
            {
                varTypeStr = "u" + varTypeStr;
            }
            getNextTokn(); //must be an Identifier
            matchCurrentTokn(TokenType.tVarIdent); //0.6.8
            if (prefix == "")
                identname = CurrentToken.Value;
            else
                identname = prefix +"." + CurrentToken.Value;
            if (varNameUsedAlready(identname))
            {
                error(CurrentToken, " tthe name  " + identname + " has been used already");
                return false;
            }
            int identifierPosn = toknIndex;
            getNextTokn();
            while (!backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                if (CurrentToken.Type == TokenType.tAssign) //an assignment will mean mem is either data or stack depending on the scope
                {
                    if (ScopeLevel==0) //global and is initialised  **0.7.2 changes to mem model
                        Memory.declVariable(identname, varTypeStr, MemArea.Data, ScopeLevel, ScopeName); //allocate data area for it
                    else 
                        Memory.declVariable(identname, varTypeStr, MemArea.Stack, ScopeLevel, ScopeName); //allocate stack memory for it
                    getNextTokn(); 
                    //assign number to var
                    //go back varIdent and do assignment
                    toknIndex = --identifierPosn;
                    ClockTicks -= 2; //stepping back upsets the timing
                    getNextTokn();
                    ParseAssignment(out value, out vartype); //when parsed leaves us at the next token
                    if (CurrentToken.Type == TokenType.tComma)
                    {
                        getNextTokn();
                        matchCurrentTokn(TokenType.tVarIdent);
                        identname = CurrentToken.Value; //get the name
                        identifierPosn = toknIndex;
                        getNextTokn();
                    }
                    else //after an assignment a comma can follow but otherwise we have finished
                        return true;
                }
                else if (CurrentToken.Type == TokenType.tComma) //not an assignment so will go on BSS or stack depending on scope
                {
                    if (ScopeLevel == 0) //global and is NOT initialised  **0.7.2 changes to mem model
                        Memory.declVariable(identname, varTypeStr, MemArea.BSS, ScopeLevel, ScopeName); //allocate data area for it
                    else
                        Memory.declVariable(identname, varTypeStr, MemArea.Stack, ScopeLevel, ScopeName); //allocate stack memory for it
                    getNextTokn();
                    matchCurrentTokn(TokenType.tVarIdent);
                    if (prefix == "")
                        identname = CurrentToken.Value;
                    else
                        identname = prefix + "." + CurrentToken.Value;

                    identifierPosn = toknIndex;
                    if (varNameUsedAlready(identname))
                    {
                        error(CurrentToken, " tthe name  " + identname + " has been used already");
                        return false;
                    }
                    getNextTokn();
                }
                else if (CurrentToken.Type == TokenType.tOpenSquareBracket) //an array is being declared
                {
                    List<int> sizes;
                    sizes = new List<int>();
                    getArraySizes(sizes);
                    //0.7.2
                    if (CurrentToken.Type == TokenType.tAssign && ScopeLevel==0) //a global initialised array so it goes into MemData 
                    {
                        buildArrays(sizes, identname, MemArea.Data, varTypeStr, -1, "");
                    }
                    else if (ScopeLevel == 0) //not initialised but global so it goes into bss
                        buildArrays(sizes, identname, MemArea.BSS, varTypeStr, -1, "");
                    else //put it on the stack
                        buildArrays(sizes, identname, MemArea.Stack, varTypeStr, -1, "");
                    foreach (int i in sizes)
                        if (i == 0)
                            return false;                           //found a [0] !!
                    int index = sizes.Count - 1; //start with last
                    if (CurrentToken.Type == TokenType.tAssign)
                    {
                        VarType totype = getVarType(varTypeStr);
                        getNextTokn();
                        //int i;
                        if (CurrentToken.Type == TokenType.tOpenBrace)
                        {
                            initArrayValues(sizes, identname, totype, -1, ""); //recursive array value reader
                            matchCurrentTokn(TokenType.tCloseBrace);
                            while (CurrentToken.Type == TokenType.tCloseBrace) //to eat the last } in multidimensional arrays
                                getNextTokn();
                        }
                        else if (CurrentToken.Type == TokenType.tStringConst)
                        {
                            Memory.writeValue(identname, CurrentToken.Value, ScopeLevel, ScopeName);
                        }
                    }
                    getNextTokn();
                    break;
                }
                else if (CurrentToken.Type == TokenType.tCR) //no assignment made so depends on the scope as BSS or Stack
                {
                    if (ScopeLevel == 0) //global and is NOT initialised  **0.7.2 changes to mem model
                        Memory.declVariable(identname, varTypeStr, MemArea.BSS, ScopeLevel, ScopeName); //allocate data area for it
                    else
                        Memory.declVariable(identname, varTypeStr, MemArea.Stack, ScopeLevel, ScopeName); //allocate stack memory for it
                    getNextTokn();
                    break;
                }
                else if (CurrentToken.Type == TokenType.tLF) //tEOL: //finished           
                    return true;
                else if (CurrentToken.Type == TokenType.tSemicolon) //ignore 
                {
                    getNextTokn();
                }
                else     //unrecognised tokn
                {
                    error(CurrentToken, " found something unexpected ");
                    return false;
                }
            }
            return true;
        }
        VarType getPtrType(VarType vartype)
        {
            switch (vartype)
            {
                case VarType.int8_t:
                case VarType.uint8_t:
                case VarType.avrString:
                case VarType.ptr1byteaddr:
                    return VarType.ptr1byteaddr;
                case VarType.int16_t:
                case VarType.uint16_t:
                case VarType.ptr2byteaddr:
                    return VarType.ptr2byteaddr;
                case VarType.int32_t:
                case VarType.uint32_t:
                case VarType.avrFloat:
                case VarType.ptr4byteaddr:
                    return VarType.ptr4byteaddr;
            }
            return vartype;
        }
        string getPtrType(string vartype)
        {
            switch (vartype)
            {
                case "int8_t":
                case "uint8_t":
                case "avrString":
                case "ptr1byteaddr":
                    return "ptr1byteaddr";
                case "int16_t":
                case "uint16_t":
                case "ptr2byteaddr":
                    return "ptr2byteaddr";
                case "int32_t":
                case "uint32_t":
                case "avrFloat":
                case "ptr4byteaddr":
                    return "ptr4byteaddr";
            }
            return vartype;
        }
        bool varPointerDeclaration(out string identname, out dynamic value, out VarType vartype)
        {
            // u8 * ptr
            value = null;
            vartype = getVarType(CurrentToken.Value);
            vartype = getPtrType(vartype);
            getNextTokn();
            matchCurrentTokn(TokenType.tPointer);
            getNextTokn();
            identname = CurrentToken.Value;
            //Memory.declVariable(identname, vartype.ToString(), MemArea.Data, ScopeLevel, ScopeName); //removed 0.7.2 fo rcode below 
            if (ScopeLevel == 0) //global and is NOT initialised  **0.7.2 changes to mem model
                Memory.declVariable(identname, vartype.ToString(), MemArea.BSS, ScopeLevel, ScopeName); //allocate data area for it
            else
                Memory.declVariable(identname, vartype.ToString(), MemArea.Stack, ScopeLevel, ScopeName); //allocate stack memory for it
            if (Tokens[toknIndex + 1].Type != TokenType.tAssign) //next token is assignment return else get next token
                getNextTokn();
            return true;
        }
        /// <summary>
        /// get the LHS, get the assignment operator, RDP the RHS,
        ///  assign RHSvalue and RHStype to LHSvalue and LHStype
        /// </summary>
        /// <param name="value"></param>
        /// <param name="vartype"></param>
        /// <returns></returns>
        bool ParseAssignment(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-ParseAssignment");
            value = null;
            vartype = VarType.none;
        //    Breaktype breaktype;
            byte registervalue; //for casting values into the registers
            //string dereferencedVarName = "";

            //VarType ptrtype = VarType.ptr1byteaddr; //default 1 or none??
            ptrOffset = 1; //this should probably be passed down the RDP rather than a global
            //bool derefernceFlag = false;

            // 1. get the LHS, keep the identifier name
            string identName = "";
            Token identToken = CurrentToken; //used for error reporting 
            bool exists = false;
            switch (CurrentToken.Type)
            {
                case TokenType.tVarIdent:// pass var, arr[],   var.y (struct)
                    identName = CurrentToken.Value;
                    exists = readValFromMemory(ref identName, out value, out vartype);
                    if (vartype == VarType.ptr2byteaddr)
                        ptrOffset = 2;
                    if (vartype == VarType.ptr4byteaddr)
                        ptrOffset = 4;
                    break;
                case TokenType.tRegAddress:
                    identName = CurrentToken.Value;
                    exists = Registers.ReadRegister(CurrentToken.Value, out registervalue);
                    if (!exists)
                    {
                        error(CurrentToken, "no Register of that name exists");
                        return false;
                    }
                    value = registervalue;
                    vartype = VarType.uint8_t;
                    break;
                case TokenType.tDereferenceOp: //e.g. *ptr need a var with a value in it
                    getNextTokn();
                    matchCurrentTokn(TokenType.tVarIdent);
                    identName = CurrentToken.Value;
                    //derefernceFlag = true;
                    exists = dereferenceAPtr(identName, out identName, out value, out vartype); //pass a ptrname, return a varname
                    break;
                default:
                    error(CurrentToken, "unexpected");
                    break;
            }
            getNextTokn();

            //if its not an assignment just return the found value and type
            if (!isAssignment(CurrentToken))
            {
                return true;
            }

            //capture Assignment type e.g. - += ++ etc etc 
            TokenType assignmenttokentype = CurrentToken.Type;

            //find the RHS
            dynamic RHSvalue = null; //for getting values from the RDP
            VarType RHStype = VarType.none;
            //getNextTokn();
            //bool result = recurseDescentParser(out RHSvalue, out RHStype);
            //if (!result)
            //{
            //    //error(CurrentToken, " cannot parse right hand side of assignment");
            //    return false;
            //}

            bool postfix = false;
            //apply the assignment
            while (isAssignment(CurrentToken))
            {
                switch (assignmenttokentype)
                {
                    case TokenType.tAssign: //=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value = RHSvalue;
                        break;
                    case TokenType.tPostIncrement: //0.7.1 changed from tIncrement  
                        postfix = true;    
                        //getNextTokn();
                        //if (isAssignment(Tokens[toknIndex + 1]))
                        //    ParseAssignment(out RHSvalue, out RHStype);
                        //else
                        recursiveDescentParser(out RHSvalue, out RHStype);
                        value = RHSvalue;
                        //ptrOffset = 1;
                        break;
                    case TokenType.tPostDecrement: //0.7.1 changed from tDecrement -- should return the 
                        postfix = true;    
                        //getNextTokn(); //removed 0.7.1
                        //if (isAssignment(Tokens[toknIndex + 1]))
                        //    ParseAssignment(out RHSvalue, out RHStype);
                        //else
                        recursiveDescentParser(out RHSvalue, out RHStype);
                        value = RHSvalue; //if a pointer then subtract the exact number from the address
                        //ptrOffset = 1;
                        break;
                    case TokenType.tPreIncrement: //0.7.1 changed from tIncrement  ++
                        recursiveDescentParser(out RHSvalue, out RHStype);
                        value += ptrOffset; //if a pointer then add the exact number to the address 
                        //ptrOffset = 1;
                        break;
                    case TokenType.tPreDecrement: //0.7.1 changed from tDecrement --
                        recursiveDescentParser(out RHSvalue, out RHStype);
                        value -= ptrOffset; //if a pointer then subtract the exact number from the address
                        //ptrOffset = 1;
                        break;
                    case TokenType.tAddAssign: //+=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value += RHSvalue * ptrOffset; //if a pointer then add the exact number to the address
                        ///ptrOffset = 1;
                        break;
                    case TokenType.tSubtractAssign: //-=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value -= RHSvalue * ptrOffset; //if a pointer then subtract the exact number from the address
                        //ptrOffset = 1;
                        break;
                    case TokenType.tMultiplyAssign: //*=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value *= RHSvalue;
                        break;
                    case TokenType.tDivideAssign:  // /=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value /= RHSvalue;
                        break;
                    case TokenType.tModulusAssign:  // %=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value %= RHSvalue;
                        break;
                    case TokenType.tBitwiseAndAssign:
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value &= RHSvalue;
                        break;
                    case TokenType.tBitwiseOrAssign:
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value |= RHSvalue;
                        break;
                    case TokenType.tBitwiseExOrAssign:
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value ^= RHSvalue;
                        break;
                    case TokenType.tShiftLeftAssign:  //0.7.1  <<=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value <<= RHSvalue;
                        break;
                    case TokenType.tShiftRighttAssign: //0.7.1  >>=
                        getNextTokn();
                        if (isAssignment(Tokens[toknIndex + 1]))
                            ParseAssignment(out RHSvalue, out RHStype);
                        else
                            recursiveDescentParser(out RHSvalue, out RHStype);
                        value >>= RHSvalue;
                        break;
                }
            }
            //check types to see if auto cast reqd or if warning required
            forceCast(vartype, RHStype, out vartype, value, out value);
            bool result = true;
            if (!postfix) //0.7.4 postfixes have already been assigned to memory
                result =  assignValue(identName, value, vartype);
            ptrOffset = 1; 
            //now the new value is available assign it to the varident
            return result; //identname if an array willbe arr[x] or dereferenced if a ptr
        }

        /// <summary>
        /// Assign a value into an identifier
        /// this could be a variable, arr, ptr, register, EEPROM
        /// </summary>
        /// <param name="identName"></param>
        /// <param name="value"></param>
        /// <param name="vartype"></param>
        /// <returns></returns>
        bool assignValue(string identName, dynamic value, VarType vartype)
        {
            if (Memory.VariableExists(identName, ScopeLevel, ScopeName))
            {
                return Memory.writeValue(identName, value, ScopeLevel, ScopeName);
            }
            else if (Registers.RegisterExists(identName))
            {
                Registers.WriteRegister(identName, (byte)value);
                return true;
            }
            else
                return false;
        }

        bool isAssignment(Token t)
        {
            if (t.Type == TokenType.tAssign |
                t.Type == TokenType.tAddAssign |
                t.Type == TokenType.tSubtractAssign |
                t.Type == TokenType.tMultiplyAssign |
                t.Type == TokenType.tDivideAssign |
                t.Type == TokenType.tModulusAssign |
                t.Type == TokenType.tShiftLeftAssign |   //0.7.1   <<=
                t.Type == TokenType.tShiftRighttAssign | //0.7.1   >>=
                t.Type == TokenType.tBitwiseAndAssign |
                t.Type == TokenType.tBitwiseOrAssign |
                t.Type == TokenType.tBitwiseExOrAssign |
                t.Type == TokenType.tPostIncrement | //changed from tIncrement 0.7.1
                t.Type == TokenType.tPostDecrement|
                t.Type == TokenType.tPreIncrement | //added 0.7.1
                t.Type == TokenType.tPreDecrement)
                return true;
            else
                return false;
        }

        #region arrays
        /// <summary>
        /// parses values to be written into an array 
        /// </summary>
        /// <param name="sizes">List holds the number of elements in the array</param>
        /// <param name="identname"></param>
        /// <param name="totype"></param>
        /// <param name="n"></param>
        /// <param name="str"></param>
        void initArrayValues(List<int> sizes, string identname, VarType totype, int n, string str)
        {
            dynamic fromvalue;
            dynamic tovalue;
            string t = str;
            n++;
            if (n >= sizes.Count)
            {
                //Console.WriteLine(identname+str);
                getNextTokn();      //will be number/char/sign
                while (CurrentToken.Type == TokenType.tOpenBrace || CurrentToken.Type == TokenType.tCloseBrace || CurrentToken.Type == TokenType.tComma)
                    getNextTokn();
                recursiveDescentParser(out fromvalue, out totype); //gets the next token
                coerceType(totype, totype, fromvalue, out tovalue);
                Memory.writeValue(identname + str, tovalue, ScopeLevel, ScopeName);
                return;
            }
            for (int i = 0; i < sizes[n]; i++)
            {
                str = "[" + i + "]";
                initArrayValues(sizes, identname, totype, n, t + str);
            }
        }
        void getArraySizes(List<int> sizes)
        {
            //0.7.4
            //two options: array has sizes declared or just []  then we need to read the size, it will be the number of ','+1 between the {} **** only handle single dimensional array this way
            if (Tokens[toknIndex + 1].Type == TokenType.tCloseSquareBracket)
            {
                int ar_start = findNextTokenOfType(toknIndex, TokenType.tOpenBrace);
                int ar_end = findNextTokenOfType(ar_start,TokenType.tCloseBrace);
                if (ar_end==0)//not found!!
                    error(CurrentToken, " no matching closing brace ");
                int size = 0;
                while (ar_start < ar_end)
                {
                    if (Tokens[ar_start].Type == TokenType.tComma)
                        size++;
                    ar_start++;
                }
                size++;
                sizes.Add(size);
                getNextTokn();
                matchCurrentTokn(TokenType.tCloseSquareBracket);
                getNextTokn();

            }
            else
            {
                while (CurrentToken.Type == TokenType.tOpenSquareBracket)
                {
                    getNextTokn();
                    matchCurrentTokn(TokenType.tIntConst);
                    int sizeOfArray = 0;
                    Int32.TryParse(CurrentToken.Value, out sizeOfArray);
                    sizes.Add(sizeOfArray);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseSquareBracket);
                    getNextTokn();
                }
            }
        }
        /// <summary>
        /// get an identifier to allow building of an array
        /// </summary>
        /// <param name="currentToken"> passes token so that errors are reported correctly</param>
        /// <returns>string e.g. arr[2][4]</returns>
        string getArrIdent(Token currentToken)
        {
            //this will be passed the token with the varident in it and build a string
            //e.g. arr[3][3] as tokens will return "arr[3][3]" 
            // arr[i][j] will look up the contenst of i(e.g 9) and j(eg4) and return a string "arr[9][4]" 
            string arrident = currentToken.Value; //the varident
            getNextTokn(); //"["
            while (CurrentToken.Type == TokenType.tOpenSquareBracket)
            {
                arrident += "[";
                getNextTokn(); //this may be a constant, a var, a number
                dynamic value;
                VarType vartype = VarType.none;
                recursiveDescentParser(out value, out vartype);
                int i;
                bool result = Int32.TryParse(value.ToString(), out i);
                if (!result)
                    error(currentToken, "there is a problem with the array index, it is badly formed ");
                if (i < 0)
                    error(currentToken, " the value is less than 0, this will not work");
                arrident += i.ToString();
                matchCurrentTokn(TokenType.tCloseSquareBracket);
                arrident += "]";
                getNextTokn();
            }
            toknIndex -= 2; //must go back and leave currenttoken as "]"
            getNextTokn();
            return arrident;
        }
        void buildArrays(List<int> sizes, string identname, MemArea memarea, string varTypeStr, int n, string str)
        {
            MemArea m = memarea;
            string t = str;
            n++;
            if (n >= sizes.Count)
            {
                //Console.WriteLine(identname+str);
                Memory.declVariable(identname + str, varTypeStr, m, ScopeLevel, ScopeName);
                return;
            }
            for (int i = 0; i < sizes[n]; i++)
            {
                str = "[" + i + "]";
                buildArrays(sizes, identname, m, varTypeStr, n, t + str);
            }
        }
        #endregion

        /// <summary>
        /// Reads a value from a variable e.g. i or arr[2] or arr[i][j]  
        /// identname is updated to include the any full array identifier
        /// </summary>
        /// <param name="identName"></param>
        /// <param name="value"></param>
        /// <param name="vartype"></param>
        /// <returns>true if memory found false if not</returns>
        bool readValFromMemory(ref string identName, out dynamic value, out VarType vartype)
        {
            value = null;
            vartype = VarType.none;
            if (toknIndex < Tokens.Count && Tokens[toknIndex + 1].Type == TokenType.tOpenSquareBracket) //LL1
            {
                identName = getArrIdent(CurrentToken);//find the full decl of the array e.g. arr[2][3]...
            }
            if (Memory.VariableExists(identName, ScopeLevel, ScopeName)) //see if the ident is in memory 
            {
                Variable v = Memory.getVariable(identName, ScopeLevel, ScopeName);
                value = v.Value;
                vartype = v.Type; //assign vartype with the type of the vaiable already stored in RAM
            }
            else if (Memory.ArrayExists(identName, ScopeLevel, ScopeName)) //see if the ident refers to an array e.g. 'arr' will try and find 'arr[0]'
            {
                getNextTokn();
                matchCurrentTokn(TokenType.tAssign);
                getNextTokn();
                matchCurrentTokn(TokenType.tStringConst);
                return Memory.writeValue(identName, CurrentToken.Value, ScopeLevel, ScopeName);
            }
            else
            {
                error(CurrentToken, "cannot find a 'variable', 'const' or '#define' of name:  " + CurrentToken.Value);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Begins to popluate an instance of FunctionDeclaration and adds it to the Functions dictionary
        /// then goes looking for the full function declaration
        /// note this is called when currenttoken is still set to the func return type not the fucn name
        /// FuncProt =	Type FuncIdent "("   [ (Type VarIdent )  {"," (Type VarIdent )} ]  ")" ";"
        /// </summary>
        #endregion

        #region functions
        void parseISRDecl()
        {
            string isrName;
            int isrLineNumber = CurrentToken.LineNumber;
            int openbrace;
            if (parsetree != null) parsetree.SafeAddtext("parseISRDecl");
            getNextTokn();
            matchThenGetNextTokn(TokenType.tOpenBracket);
            matchCurrentTokn (TokenType.tVarIdent); //e.g. INT0_vect
            isrName = CurrentToken.Value;
            getNextTokn();
            matchThenGetNextTokn(TokenType.tCloseBracket);
            while (toknIndex < Tokens.Count && (Tokens[toknIndex].Type == TokenType.tCommand || Tokens[toknIndex].Type == TokenType.tComment)) //igone comments and viz commands on this line
            {
                getNextTokn();
            }
            if (CurrentToken.Type == TokenType.tCR)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tLF)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tOpenBrace)
            {
                openbrace = toknIndex;
                toknIndex = findMatchingBrace(toknIndex);
            }
            FunctionDefinition isrDefn = new FunctionDefinition(isrName);
            isrDefn.ReturnType = "void";
            isrDefn.DeclarationLineNumber = isrLineNumber;
            bool unique = Functions.AddFunctionDefinition(isrDefn);
            getNextTokn ();
            if (Tokens[toknIndex].Type == TokenType.tCR) //ignore any tCR 
                toknIndex++;
            if (Tokens[toknIndex].Type == TokenType.tLF) //ignore any t
                getNextTokn();

        }
        void parseFuncProt()//note this is called when currenttoken is still set to the func return type not the func name
        {
            if (parsetree != null) parsetree.SafeAddtext("ParseFuncProt");
            //get the returntype, name and line number
            string returntype = CurrentToken.Value;
            getNextTokn(); //gets the function name
            string funcName = CurrentToken.Value;
            int prototypeLineNumber = CurrentToken.LineNumber;
            Functions.AddFunctionDefinition(returntype, funcName, prototypeLineNumber, 0);

            getNextTokn();
            matchCurrentTokn(TokenType.tOpenBracket);
            getNextTokn();
            if (CurrentToken.Value.ToLower() == "void") //skip void 0.6.5
                getNextTokn();
            //get any parameters
            string parametertypestring;
            while (!backgroundWorkerMe.CancellationPending && CurrentToken.Type == TokenType.tType)
            {
                parametertypestring = CurrentToken.Value; //the type
                getNextTokn(); //could be tVarIdent or tPointer
                if (CurrentToken.Type == TokenType.tPointer)
                {
                    getNextTokn();
                    matchCurrentTokn(TokenType.tVarIdent);
                    string ptrtype = getPtrType(parametertypestring);
                    Functions.AddParameter(funcName, ptrtype, CurrentToken.Value);
                }
                else if (CurrentToken.Type == TokenType.tVarIdent)
                {
                    Functions.AddParameter(funcName, parametertypestring, CurrentToken.Value);
                }
                else
                {
                    error(CurrentToken, "expected a variable");
                }
                getNextTokn();
                if (CurrentToken.Type == TokenType.tCloseBracket)
                    break;
                else
                    matchCurrentTokn(TokenType.tComma);
                getNextTokn();
            }
            matchCurrentTokn(TokenType.tCloseBracket);
            getNextTokn();


            //look for the actual function declaration
            int index = toknIndex;
            FunctionDefinition defn = null;
            while (!backgroundWorkerMe.CancellationPending && index < Tokens.Count) //look through all the tokens until we get to the function definition
            {
                if (Tokens[index].Type == TokenType.tFuncDecl && Tokens[index].Value == funcName) //the funcdefn found matches the funcprot
                {
                    defn = Functions.GetFunction(funcName);
                    //check that the fundefn return type matches funcprot
                    if (Tokens[index - 1].Value != returntype)
                    {
                        error(prototypeLineNumber, " the prototype has a return type of " + returntype + " and the function declaration has a return type of  " + Tokens[index - 1].Value);
                        break;
                    }
                    //names and return types match - find the place for execution to start
                    defn.DeclarationLineNumber = Tokens[index].LineNumber;
                    index++;
                    if (Tokens[index].Type != TokenType.tOpenBracket)
                    {
                        error(prototypeLineNumber, "Line:" + Tokens[index].LineNumber + " there should be a '(' in the declaration of " + funcName);
                        break;
                    }
                    index++;
                    if (Tokens[index].Value == "void")
                        index++;
                    //if no parameters in the defn and none in the prot then its ok and exit
                    if (defn.Parameters.Count == 0 && Tokens[index].Type == TokenType.tCloseBracket)
                    {
                        index++;
                        //finished parameter collection store the executing token
                        //fisr skip any tCommands
                        if (Tokens[index].Type == TokenType.tCommand)
                        {
                            while (!backgroundWorkerMe.CancellationPending && Tokens[index].Type == TokenType.tCommand)
                            {
                                index++;
                            }
                        }
                        //skip CR LF
                        //if (index < Tokens.Count && Tokens[index].Type == TokenType.tCR) index++;
                        //if (index < Tokens.Count && Tokens[index].Type == TokenType.tLF) index++;
                        //defn.ExecutingLineNumber = Tokens[index].LineNumber;
                        return;
                    }
                    if (defn.Parameters.Count == 0)
                    {
                        error(CurrentToken, "The parametrs do not match between the Prototype and the Declaration");
                        error(defn.DeclarationLineNumber, "The parametrs do not match between the Prototype and the Declaration");
                    }
                    //otherwise some parameters in the prototype
                    int paramIndex = 0;
                    Parameter p;
                    //check each parameter TYPE and NAME matches the tokens found
                    while (index < Tokens.Count && paramIndex < defn.Parameters.Count)
                    {
                        p = defn.Parameters[paramIndex];  //get a parameter from the saved func definition
                        if (Tokens[index].Type != TokenType.tType) //first make sure the func defn
                            error(prototypeLineNumber, " found  " + Tokens[index].Type + " and was expecting a type");
                        if (p.Type.Contains("ptr")) //we are expecitng the type to be a ptr... so convert the param in the func decl to a ptr type to do compare
                        {
                            string funcdefnptrtype = getPtrType(Tokens[index].Value);
                            if (funcdefnptrtype != p.Type) //then that it matches
                                error(prototypeLineNumber, " the prototype has a type of  " + p.Type + " and the function declaration has a type of  " + Tokens[index].Value);
                        }
                        else //not passing a ptr so compare types and make sure that the func doesnt have a pointer
                        {
                            if (Tokens[index].Value != p.Type) //then that it matches
                                error(prototypeLineNumber, " the prototype has a type of  " + p.Type + " and the function declaration has a type of  " + Tokens[index].Value);
                            if (Tokens[index+1].Type == TokenType.tPointer)
                                error(prototypeLineNumber, " the prototype has a type of  " + p.Type + " and the function declaration has a pointer of type  " + Tokens[index].Value);
                        }
                        index++;
                        if (Tokens[index].Type == TokenType.tPointer)
                            index++;
                        p = defn.Parameters[paramIndex];
                        //check the identifiers match
                        if (Tokens[index].Type != TokenType.tVarIdent) //next make sure its a varIdent
                            error(prototypeLineNumber, " found  " + Tokens[index].Type + " and was expecting a variable name");
                        if (Tokens[index].Value != p.Name) //and that the names match
                            error(CurrentToken, " tthe prototype has a variable name of " + p.Name + " and tthe function declaration has a name of " + Tokens[index].Value);
                        index++;
                        paramIndex++;
                        if (Tokens[index].Type == TokenType.tCloseBracket && paramIndex == defn.Parameters.Count) //we have all the params in the decl and there are also no more in the prot
                            break;
                        else if (Tokens[index].Type == TokenType.tComma && paramIndex != defn.Parameters.Count)
                            index++;
                        else
                            error(CurrentToken, "tthe parametrs do not match between tthe Prototype and tthe Declaration");
                    }
                    index++;
                    return;
                }
                index++;

            } //got to the end and didnt find a match
            error(CurrentToken, "Could not find the function " + funcName);
            index++;
        }
        void parseFuncDecl() //note this is called when currenttoken is still set to the func return type not the func name
        {
            if (parsetree != null) parsetree.SafeAddtext("parseFuncDecl");
            //get the returntype, name and line number, add func to functions table
            string returntype = CurrentToken.Value;
            getNextTokn();
            string funcName = CurrentToken.Value;
            int declarationLineNumber = CurrentToken.LineNumber;
            FunctionDefinition funcDefn = new FunctionDefinition(funcName);
            funcDefn.ReturnType = returntype;
            funcDefn.DeclarationLineNumber = declarationLineNumber;
            bool unique = Functions.AddFunctionDefinition(funcDefn);
            getNextTokn();
            matchCurrentTokn(TokenType.tOpenBracket);
            getNextTokn();
            if (CurrentToken.Value.ToLower() == "void") //skip void 0.6.5
                getNextTokn();

            //get any paramters
            string parametertype;
            while (!backgroundWorkerMe.CancellationPending && CurrentToken.Type == TokenType.tType)
            {
                parametertype = CurrentToken.Value;
                getNextTokn();
                if (CurrentToken.Type == TokenType.tPointer)
                {
                    getNextTokn();
                    matchCurrentTokn(TokenType.tVarIdent);
                    parametertype = getPtrType(parametertype);//getthe ptr equiv of this
                }
                else
                {
                    matchCurrentTokn(TokenType.tVarIdent);
                }
                if (unique)
                    Functions.AddParameter(funcName, parametertype, CurrentToken.Value);
                getNextTokn();
                if (CurrentToken.Type == TokenType.tCloseBracket)
                    break;
                else
                    matchCurrentTokn(TokenType.tComma);
                getNextTokn();
            }
            matchCurrentTokn(TokenType.tCloseBracket);
            getNextTokn();
            //skip to CR
            while (toknIndex < Tokens.Count && (Tokens[toknIndex].Type == TokenType.tCommand || Tokens[toknIndex].Type == TokenType.tComment))
            {
                getNextTokn();
            }
            if (Tokens[toknIndex].Type == TokenType.tCR) //ignore any tCR between while() and trueStat
                getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tLF) //ignore any tCR between while() and trueStat
                getNextTokn();
            //funcDefn.ExecutingLineNumber = Tokens[toknIndex].LineNumber;

            matchCurrentTokn(TokenType.tOpenBrace);
            toknIndex = findMatchingBrace(toknIndex);
            getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tCR) //ignore any tCR 
                toknIndex++;
            if (Tokens[toknIndex].Type == TokenType.tLF) //ignore any t
                getNextTokn();
        }
        void parseFuncCall(string funcname, out dynamic returnvalue, out VarType returnVartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("parseFuncCall");
            //ebnf: FuncIdent "("   [ ([&]VarIdent | Number)  {"," ([&]VarIdent | Number)} ]  ")" ";"
            //these values definethre state of the viz on entry and must be returned to this state on exit 
            bool entrystate_NoViz = NoViz;
            bool entrystate_NoStepDelay = NoStepDelay;
            bool entrystate_NoTrace = NoTrace;
            string entrystateScopeName = ScopeName;

            returnvalue = null;
            returnVartype = VarType.none;
            Breaktype breaktype = Breaktype.None;

            //FunctionDefinition funcCopy = Functions.GetFunction(CurrentToken.Value);
            FunctionDefinition funcCopy = Functions.GetFunction(funcname);
            if (funcCopy == null) //cannot copy the function
            {
                getNextTokn();
                return;
            }

            //change the scopename for the declaration of the return variable and any passed variables, then change it back
            ScopeLevel++;
            ScopeName = funcCopy.Name;
            Memory.AddStackPointerToMem(Tokens[toknIndex].LineNumber, MemArea.Stack, ScopeLevel, ScopeName);
            Memory.declVariable("ReturnValue", funcCopy.ReturnType, MemArea.Stack, ScopeLevel, ScopeName);
            foreach (Parameter p in funcCopy.Parameters)
            {
                Memory.declVariable(p.Name, p.Type, MemArea.Stack, ScopeLevel, ScopeName); //0.7.2. changed to stack
            }
            ScopeLevel--; //change back to correct scope
            ScopeName = entrystateScopeName;
            getNextTokn();
            matchCurrentTokn(TokenType.tOpenBracket);
            //get each var passed to the func and check that the qty and type matches that in the funcdecl, put each one into the funcdefn
            getNextTokn();
            while (!backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                //if no parameters in the defn and none in the prot then its ok and exit
                if (funcCopy.Parameters.Count == 0 && Tokens[toknIndex].Type == TokenType.tCloseBracket)
                    break;
                if (funcCopy.Parameters.Count == 0)
                    error(CurrentToken, "The number of parameters do not match between calling tthe function and tthe function declaration");
                //otherwise some parameters in the prototype
                int paramIndex = 0;
                Parameter p;
                VarType paramtypeinfuncdecl; //the type expected by the function
                dynamic paramvalue; //the value encountered in the List
                VarType paramtype = VarType.none; //the type of the value encounterd in the list

                //check each parameter TYPE and NAME matches the tokens found, if not then do coerce/truncate
                while (!backgroundWorkerMe.CancellationPending && paramIndex < funcCopy.Parameters.Count)
                {
                    p = funcCopy.Parameters[paramIndex]; //the parameter in the function declaration
                    paramtypeinfuncdecl = getVarType(p.Type); //change the string in the funcdecl to a type - the type of the value(const/var) in the List should fit with this
                    //we need to try and resolve values in the func call - these names will be in the current scope (not the new one )
                    if (paramtypeinfuncdecl.ToString().Contains("ptr"))
                    {
                        matchCurrentTokn(TokenType.tReferenceOp);// &
                        recursiveDescentParser(out paramvalue, out paramtype); //parse the parameter found, should return an address of the var
                        ScopeLevel++; ScopeName = funcCopy.Name;
                        Memory.writeValue(p.Name, paramvalue, ScopeLevel, ScopeName);
                        ScopeLevel--; ScopeName = entrystateScopeName; //revert back to current scope
                    }
                    else
                    {
                        recursiveDescentParser(out paramvalue, out paramtype); //parse the parameter found
                        dynamic tovalue = null;
                        bool result = coerceType(paramtype, paramtypeinfuncdecl, paramvalue, out tovalue); //try and put the value encountered in the param into the funcdefn
                        //write the passed value into the memory - these variables are in the new scope
                        ScopeLevel++; ScopeName = funcCopy.Name;
                        Memory.writeValue(p.Name, paramvalue, ScopeLevel, ScopeName);
                        ScopeLevel--; ScopeName = entrystateScopeName; //revert back to current scope
                        if (!result)
                        {
                            //the error has been reported by coerceType()
                            break; //see what a break achieves for us ???
                        }
                    }
                    paramIndex++;
                    if (CurrentToken.Type == TokenType.tCloseBracket && paramIndex == funcCopy.Parameters.Count) //we have all the params in the decl and there are also no more in the prot
                        break;
                    else if (CurrentToken.Type == TokenType.tComma && paramIndex != funcCopy.Parameters.Count)
                        getNextTokn();
                    else
                        error(CurrentToken, "tthe number of parameters do not match between calling tthe function and tthe function declaration");
                }
                break;
            }
            int returntoken = toknIndex; //the closing bracket of the function call
            getNextTokn();
            //at this stage determine what to do with the viz for this function
            if (CurrentToken.Type == TokenType.tSemicolon)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tCR)
                getNextTokn();

            //jump to the function
            ScopeName = funcCopy.Name;
            ScopeLevel++;
            toknIndex = findindexofTokenOnLine(TokenType.tCloseBracket, funcCopy.DeclarationLineNumber);//consume tokens till ')' 
            toknIndex++; //go to next token
            if (Tokens[toknIndex].Type == TokenType.tCommand)
            {
                while (!backgroundWorkerMe.CancellationPending && Tokens[toknIndex].Type == TokenType.tCommand)
                {
                    if (Tokens[toknIndex].Value == "noviz") NoViz = true;
                    if (Tokens[toknIndex].Value == "nostepdelay") NoStepDelay = true;
                    if (Tokens[toknIndex].Value == "notrace") NoTrace = true;
                    toknIndex++;
                    if (Tokens[toknIndex].Type == TokenType.tCR)
                        toknIndex++;
                    if (Tokens[toknIndex].Type == TokenType.tLF)
                        toknIndex++;
                }
            }
            CurrentToken = Tokens[toknIndex];
            getNextTokn();
            if (CurrentToken.Type == TokenType.tCR)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tLF)
                getNextTokn();

            if (CurrentToken.Type == TokenType.tOpenBrace)
                getNextTokn();

            while (!backgroundWorkerMe.CancellationPending && CurrentToken.Type != TokenType.tCloseBrace && breaktype != Breaktype.Return) //0.5.5
            {
                parseStatement(out returnvalue, out returnVartype, out breaktype);
                if (CurrentToken.Type == TokenType.tReturn)
                {
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tSemicolon || CurrentToken.Type == TokenType.tCR || CurrentToken.Type == TokenType.tComment)
                    {
                    }
                    else
                    {
                        recursiveDescentParser(out returnvalue, out returnVartype); //carry out the function 
                        Memory.writeValue("ReturnValue", returnvalue, ScopeLevel, ScopeName);
                    }
                    getNextTokn();
                    //returntoken--;//this makes sure that any return value is calculated and shown
                    break;
                }
                //if (breaktype == Breaktype.Return)
                //    break; //we have an early return from the  function
            }

            //return to calling function
            reportProgress(true);
            toknIndex = returntoken;
            CurrentToken = Tokens[toknIndex];
            //return bool viz flags to state they were on entry
            NoViz = entrystate_NoViz;
            NoTrace = entrystate_NoTrace;
            NoStepDelay = entrystate_NoStepDelay;
            ScopeName = entrystateScopeName; //on exit return to opening scope name
            Memory.removeScope(ScopeLevel);
            ScopeLevel--; //back to the calling scope
            //getNextTokn(); 0.7.2 removed as function calls within other statements eat things they shouldnt like extra )

        }
        void parseISRCall(string ISRname)
        {
            if (parsetree != null) parsetree.SafeAddtext("parseFuncCall");
            //ebnf: FuncIdent "("   [ ([&]VarIdent | Number)  {"," ([&]VarIdent | Number)} ]  ")" ";"
            //these values definethre state of the viz on entry and must be returned to this state on exit 
            bool entrystate_NoViz = NoViz;
            bool entrystate_NoStepDelay = NoStepDelay;
            bool entrystate_NoTrace = NoTrace;
            string entrystateScopeName = ScopeName;

            //FunctionDefinition funcCopy = Functions.GetFunction(CurrentToken.Value);
            FunctionDefinition ISRfunc = Functions.GetFunction(ISRname);
            
            int returntoken = toknIndex; //where to come back to on exit of the ISR

            //jump to the function
            ScopeName = ISRfunc.Name;
            ScopeLevel++;
            toknIndex = findindexofTokenOnLine(TokenType.tCloseBracket, ISRfunc.DeclarationLineNumber);//consume tokens till ')'
            toknIndex++; //go to next token
            if (Tokens[toknIndex].Type == TokenType.tCommand)
            {
                while (!backgroundWorkerMe.CancellationPending && Tokens[toknIndex].Type == TokenType.tCommand)
                {
                    if (Tokens[toknIndex].Value == "noviz") NoViz = true;
                    if (Tokens[toknIndex].Value == "nostepdelay") NoStepDelay = true;
                    if (Tokens[toknIndex].Value == "notrace") NoTrace = true;
                    toknIndex++;
                    if (Tokens[toknIndex].Type == TokenType.tCR)
                        toknIndex++;
                    if (Tokens[toknIndex].Type == TokenType.tLF)
                        toknIndex++;
                }
            }
            CurrentToken = Tokens[toknIndex];
            getNextTokn();
            if (CurrentToken.Type == TokenType.tCR)
                getNextTokn();
            if (CurrentToken.Type == TokenType.tLF)
                getNextTokn();

            if (CurrentToken.Type == TokenType.tOpenBrace)
                getNextTokn();

            while (!backgroundWorkerMe.CancellationPending && CurrentToken.Type != TokenType.tCloseBrace) //0.5.5
            {
                parseStatement();
                if (CurrentToken.Type == TokenType.tReturn)
                {
                    getNextTokn();
                    if (CurrentToken.Type == TokenType.tSemicolon || CurrentToken.Type == TokenType.tCR || CurrentToken.Type == TokenType.tComment)
                    {
                    }
                    else
                    {
                        recursiveDescentParser(); //carry out the function 
                    }
                    getNextTokn();
                    //returntoken--;//this makes sure that any return value is calculated and shown
                    break;
                }
                //if (breaktype == Breaktype.Return)
                //    break; //we have an early return from the  function
            }

            //return to calling function
            reportProgress(true);
            toknIndex = returntoken;
            CurrentToken = Tokens[toknIndex];
            //return bool viz flags to state they were on entry
            NoViz = entrystate_NoViz;
            NoTrace = entrystate_NoTrace;
            NoStepDelay = entrystate_NoStepDelay;
            ScopeName = entrystateScopeName; //on exit return to opening scope name
            Memory.removeScope(ScopeLevel);
            ScopeLevel--; //back to the calling scope
            getNextTokn();

        }
        void ParseIf(out Breaktype breaktype)
        {
            if (parsetree != null) parsetree.SafeAddtext("parseIF");
            //when an if hits a break or a return then it must exit without absorbing the token, the token is needed by the calling structure
            breaktype = Breaktype.None;

            //1. find the exit posn for the whole if-elseif-else statement block
            getNextTokn();// move past tIF

            int count = toknIndex;  //in C this should be a '('
            int firstBoolPosn = toknIndex;
            int exitTokn = firstBoolPosn;  //once a true statement has been executed jump to here to continue on with the rest of the program code

            bool boolExprResult;
            ////1a - check bool comes next
            //count++;
            //boolExprResult = checkToknIsBool(count);
            //1b skip through the bool to find the posn of the statement after the ')'
            int trueStatPosn = findMatchingBracket(firstBoolPosn);//return posn of matching ')'
            trueStatPosn++;
            if (Tokens[trueStatPosn].Type == TokenType.tCR) //ignore EOL and go to next token
                trueStatPosn++;
            if (Tokens[trueStatPosn].Type == TokenType.tLF) //ignore EOL and go to next token
                trueStatPosn++;


            //1c skip through this statement/block to find the posn of the next statement
            count = findNextStatPosnAfterMatchingStatEnd(trueStatPosn);
            //2 - find any and all tElseIf tokns
            while (count < Tokens.Count && Tokens[count].Type == TokenType.tElseIf && !backgroundWorkerMe.CancellationPending)
            {
                //Console.WriteLine ("found tElseIf at "+count.ToString ());
                count++; //the openbracket
                if (Tokens[count].Type != TokenType.tOpenBracket)
                    error(count, "expected a '(' after an else if");
                else
                    count = findMatchingBracket(count);
                //boolExprResult = checkToknIsBool(count); //not working as there might be multiple )
                count++;
                if (Tokens[count].Type == TokenType.tCR) //ignore EOL and go to next token
                    count++;
                if (Tokens[count].Type == TokenType.tLF) //ignore EOL and go to next token
                    count++;
                count = findNextStatPosnAfterMatchingStatEnd(count); //the position after the true
            }
            //3 find a final else if it exists
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tElse)
            {
                //Console.WriteLine("found tElse at " + count.ToString());
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)//skip a tCR
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)//skip a tCR
                    count++;
                count = findNextStatPosnAfterMatchingStatEnd(count);
            }
            exitTokn = count;
            //Console.WriteLine("found exit at " + count.ToString());

            //*************do the testing *********************
            //now do loop again and execute and true statements thenn jump to exit
            //4a jump back to the beginning
            toknIndex = firstBoolPosn;
            CurrentToken = Tokens[toknIndex];
            //4b evaluate the bool, if its true then evaluate and jump to exit
            matchThenGetNextTokn(TokenType.tOpenBracket); //boolean expression should be enclosed in ()
            boolExprResult = boolexpression();
            matchCurrentTokn(TokenType.tCloseBracket); //make sure there is a closing brace but do not move currTokn to next tokn (as we need to color this line)
            if (boolExprResult) //parse a true result
            {
                ParserResult = ParserResult.True;
                getNextTokn();//move to tokn after ')'
                if (CurrentToken.Type == TokenType.tCR)
                    getNextTokn();
                if (CurrentToken.Type == TokenType.tLF)
                    getNextTokn();
                parseStatement(out breaktype);
                reportProgress(true); //0.7.4 added here as single lines without {}were not colored
                if (breaktype != Breaktype.None)
                    return; // make sure the outer structure gets the break/return 
                toknIndex = exitTokn; //gotcha with return embedded inside an if statement - same with a break
                CurrentToken = Tokens[toknIndex]; //move to this tokn
                return;
            }
            else //not true so find posn of next thing to do/check
            {
                ParserResult = ParserResult.False;
                reportProgress(true);  //to make sure line gets colored 
                toknIndex++;//move to tokn after ')'
                //CurrentToken = Tokens[++toknIndex];  
                if (Tokens[toknIndex].Type == TokenType.tCR)//dont process any tCR here as we want to color only once
                    toknIndex++;
                if (Tokens[toknIndex].Type == TokenType.tLF)
                    toknIndex++;
                toknIndex = findNextStatPosnAfterMatchingStatEnd(toknIndex);
                CurrentToken = Tokens[toknIndex]; //move to this tokn
            }
            while (CurrentToken.Type == TokenType.tElseIf && !backgroundWorkerMe.CancellationPending) //here we need to check that the backgroundWorker hasnt been cancelled
            {
                //Console.WriteLine("checking tElseIf at " + toknIndex.ToString());
                getNextTokn();
                matchThenGetNextTokn(TokenType.tOpenBracket); //boolean expression should be enclosed in ()
                boolExprResult = boolexpression();
                matchCurrentTokn(TokenType.tCloseBracket); //make sure there is a closing brace but do not move currTokn to next tokn (as we need to color this line)
                if (boolExprResult)
                {
                    ParserResult = ParserResult.True;
                    getNextTokn();//move to tokn after ')'
                    if (CurrentToken.Type == TokenType.tCR)
                        getNextTokn();
                    if (CurrentToken.Type == TokenType.tLF)
                        getNextTokn();
                    parseStatement(out breaktype);
                    reportProgress(true);//0.7.4 to make sure single lines get colored
                    if (breaktype != Breaktype.None)
                        return;
                    toknIndex = exitTokn;
                    CurrentToken = Tokens[toknIndex]; //move to this tokn
                    return;
                }
                else
                {
                    ParserResult = ParserResult.False;
                    reportProgress(true); // fireEOLEvent(); //to make sure line gets colored
                    CurrentToken = Tokens[++toknIndex]; //move to tokn after ')' //dont process any tCR here as we want to color only once
                    if (Tokens[toknIndex].Type == TokenType.tCR)
                        toknIndex++;
                    if (Tokens[toknIndex].Type == TokenType.tLF)
                        toknIndex++;
                    toknIndex = findNextStatPosnAfterMatchingStatEnd(toknIndex);
                    CurrentToken = Tokens[toknIndex]; //move to this tokn
                }
            }
            //wasnt a result above, check for tElse
            if (Tokens[toknIndex].Type == TokenType.tElse)
            {
                ParserResult = ParserResult.True;
                getNextTokn();//move to tokn after ')'
                if (CurrentToken.Type == TokenType.tCR)
                    getNextTokn();
                if (CurrentToken.Type == TokenType.tLF)
                    getNextTokn();
                parseStatement(out breaktype);
                reportProgress(true);//0.7.4 to make sure single linesget colored
                if (breaktype != Breaktype.None)
                    return;
                getNextTokn(); //***
            }

        }
        void ParseWhile()
        {
            if (parsetree != null) parsetree.SafeAddtext("parseWhile");
            //valid alternatives 
            //  while(x<12)x++;     1
            //  while(x<12){x++;}   2
            //  while(x<12)         3
            //      x++;
            //  While(x<12)         4
            //  {
            //      x++;
            //  }
            // while(1)
            // while(true)
            Breaktype breaktype;
            Token t = CurrentToken; // tWhile
            int count = toknIndex;
            int boolTestPosn = toknIndex;           // openBracket '(' position
            int trueStatToknPosn = toknIndex;       //the first tokn in the statement after the boolean (and after any tCR )
            int falseStatToknPosn = toknIndex;     //the first tokn in the statement after the while loop (and after any tCR )
            //1.must find the end of the boolExpr  - in doing that note the start of it too
            //1a. start at the 'while' search thru tokns for '(' , must get it before end of tokens and on this line
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tOpenBracket && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF)
                count++;
            boolTestPosn = count;
            CurrentToken = Tokens[count];
            matchCurrentTokn(TokenType.tOpenBracket);
            //1b find the matching closing bracket ')'
            count = findMatchingBracket(count);
            count++;
            //there may be a tCR between the ) and the statement or they could be on the same line
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR) //ignore any tCR between while() and trueStat
                count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF) //ignore any tCR between while() and trueStat
                count++;
            trueStatToknPosn = count;//where the statement for true begins
            //2. must find the statement for the false
            //if the true block is enclosed in {} then it will be the token after the closing brace that is not a tCR
            //if its not in a brace then it will be after the next tCR
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tOpenBrace)
            {
                count = findMatchingBrace(count);
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)
                    count++;
            }
            else//no enclosing braces - so could be on same line or next line, will be a single statement and should have a semicolon or CR
            {
                while (count < Tokens.Count && Tokens[count].Type != TokenType.tLF)// && Tokens[count].Type != ToknType.tSemicolon)
                {
                    count++;
                }
                count++;
                if (count >= Tokens.Count)
                    error(count, "cannot find an end to the while loop");
            }
            falseStatToknPosn = count;

            //3. carry out the boolExpr and parse stat's starting either at whileTrueStatToknPosn or whileFalseStatToknPosn
            bool boolExprResult = true;
            while (boolExprResult && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)  //here we need to check that the backgroundWorker hasnt been cancelled 
            {
                ParserResult = ParserResult.True;
                //lineColor = trueLineColor;    //for the While(...) line
                toknIndex = boolTestPosn;
                CurrentToken = Tokens[toknIndex]; //put the '(' into currentTokn
                matchThenGetNextTokn(TokenType.tOpenBracket); //boolean expression should be enclosed in ()
                boolExprResult = boolexpression();      //evaluate the bool expression including the ()
                matchCurrentTokn(TokenType.tCloseBracket); //make sure there is a closing brace but do not move currTokn to next tokn (as we need to color this line)


                //bool statementIsInBraces = false;
                //if (Tokens[trueStatToknPosn].Type == ToknType.tOpenBrace)
                //    statementIsInBraces = true;


                if (!boolExprResult)//we can leave the while loop but we still want the parser to show the test happening so fire the event first
                {
                    ParserResult = ParserResult.False;
                    reportProgress(true); //fireEOLEvent(); //we dont go back to process the CR we want to go forward end of the while, so fire event here, 
                    toknIndex = falseStatToknPosn - 1; //finished
                    CurrentToken = Tokens[toknIndex];
                    ParserResult = ParserResult.Ok;
                    //if (statementIsInBraces) //need test for logic error,needed when no {} when stepping 
                    getNextTokn();
                    //else
                    //    getNextTokn();
                    return;
                }
                getNextTokn();
                if (CurrentToken.Type == TokenType.tCR) getNextTokn();
                if (CurrentToken.Type == TokenType.tLF) parseStatement(out breaktype);
                toknIndex = trueStatToknPosn;
                CurrentToken = Tokens[toknIndex];
                //if (CurrentToken.Type == ToknType.tOpenBrace)
                //    statementIsInBraces = true;
                parseStatement(out breaktype);
                //if (statementIsInBraces) //need test for logic error,needed when no {} when stepping 
                getNextTokn(); //false
                //else
                //    getNextTokn();
            }
        }
        void parseDo()
        {
            if (parsetree != null) parsetree.SafeAddtext("parseDo");
            //"do" Statement "while" "(" boolExpr ")"
            Breaktype breaktype;
            reportProgress(true);
            int count = toknIndex;
            int boolTestPosn = toknIndex;           // openBracket '(' position
            int trueStatToknPosn = toknIndex;       //the first tokn in the statement after the boolean (and after any tCR )
            int exitStatToknPosn = toknIndex;     //the first tokn in the statement after the while loop (and after any tCR )
            // 1. find the truestatement 
            count++;
            if (Tokens[count].Type == TokenType.tCR)
                count++;
            if (Tokens[count].Type == TokenType.tLF)
                count++;
            trueStatToknPosn = count; //will be a statement to parse or a "{"
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tOpenBrace)
            {
                count = findMatchingBrace(count);
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)
                    count++;
            }
            else//no enclosing braces - so could be on same line or next line, will be a single statement and should have a semicolon or CR
            {
                while (count < Tokens.Count && Tokens[count].Type != TokenType.tLF)
                {
                    count++;
                }
                count++;
                if (count >= Tokens.Count)
                    error(count, "cannot find an end to the while loop");
            }
            toknIndex = count;
            CurrentToken = Tokens[toknIndex];
            matchThenGetNextTokn(TokenType.tWhile);
            matchThenGetNextTokn(TokenType.tOpenBracket);
            //2.find bool test position
            boolTestPosn = toknIndex;
            //3. find the exit point
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tCloseBracket && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF)
                count++;
            count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tSemicolon)
                count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)
                count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)
                count++;
            exitStatToknPosn = count;
            //4. execute the loop first even if bool is false
            bool boolExprResult = false;
            //ParserResult = Result.False;
            toknIndex = trueStatToknPosn;
            CurrentToken = Tokens[toknIndex];
            //boolExprResult = boolexpression();
            do
            {

                //reportProgress(true);
                //parse the true statement
                toknIndex = trueStatToknPosn;
                CurrentToken = Tokens[toknIndex];
                parseStatement(out breaktype);
                //reevaluate the bool
                toknIndex = boolTestPosn;
                CurrentToken = Tokens[toknIndex];
                boolExprResult = boolexpression();
                if (boolExprResult)
                {
                    ParserResult = ParserResult.True;
                    reportProgress(true);
                }
            }
            while (boolExprResult && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count);
            ParserResult = ParserResult.False;
            if (CurrentToken.Type == TokenType.tCloseBracket)
                getNextTokn();
            //reportProgress(true);
        }
        void ParseFor()
        {
            if (parsetree != null) parsetree.SafeAddtext("parseFor");
            // for (int i =0; i<10; i++){}
            // for (i =0; i<10; i++){}
            //for ( decl or assignment ; bool; statement )
            dynamic identval = null;
            Breaktype breaktype;
            string identname="";
            VarType identType;
            int count = toknIndex;
            int boolTestPosn = toknIndex;           // after first ';'
            int LoopControlVarPosn = toknIndex;           // after second ';'
            int trueStatToknPosn = toknIndex;       //the first tokn in the statement after the ')' (and after any tCR )
            int exitStatToknPosn = toknIndex;       //the first token after the '}' or after the tCR on the line
            //1. first do the declaration/initialisation for (int i = 0 ;            
            getNextTokn();
            matchCurrentTokn(TokenType.tOpenBracket);
            getNextTokn();
            string openScopeName = ScopeName;
            switch (CurrentToken.Type)
            {
                case TokenType.tType://e.g. for (int i=0; ... ; ...) { } //added 0.7.4
                    ParseDeclaration("", out identname, out identval, out identType);
                    break;
                case TokenType.tVarIdent:           //e.g. for (i=0; ... ; ...) { }
                    ParseAssignment(out identval, out identType);
                    //ParseAssignment(out identval, out identType);
                    break;
                case TokenType.tSemicolon:       //e.g. for(; ... ; ...) { }
                    //the identifier must be already initiliased or its part of a never ending loop
                    break;
                default:
                    error(CurrentToken, "expeced an identifier e.g. 'i = 0'");
                    break;
            }

            matchThenGetNextTokn(TokenType.tSemicolon);
            //2. note the bool start position
            count = toknIndex;
            boolTestPosn = count;

            //find the loop control - will be after the next semicolon
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tSemicolon && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF)
                count++;
            count++;
            LoopControlVarPosn = count;

            //find the ')' - must be before any EOL 
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tCloseBracket && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF)
                count++;
            toknIndex = count;
            CurrentToken = Tokens[toknIndex];
            matchCurrentTokn(TokenType.tCloseBracket);
            count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR) //ignore any tCR between while() and trueStat
                count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF) //ignore any tCR between while() and trueStat
                count++;
            trueStatToknPosn = count;//where the statement for true begins
            //find the statement for the exit
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tOpenBrace)
            {
                count = findMatchingBrace(count);
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)
                    count++;
            }
            else//no enclosing braces - so could be on same line or next line, or multiple lines if nested other things??? will be a single statement and should have a semicolon or CR
            {
                while (count < Tokens.Count && Tokens[count].Type != TokenType.tSemicolon && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF)
                {
                    count++;
                }
                count++;
                if (count >= Tokens.Count)
                    error(count, "cannot find an end to the for loop");
            }
            exitStatToknPosn = count; //an initial extimate of the exit of the for loop, this value is adjusted though once the inside of the loop has been parsed 

            //now we have pointers to all the statements
            //run the loop
            bool boolExprResult = true;
            while (boolExprResult && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)  //here we need to check that the backgroundWorker hasnt been cancelled 
            {
                ParserResult = ParserResult.True;
                //lineColor = trueLineColor;    //for the While(...) line
                toknIndex = boolTestPosn;
                CurrentToken = Tokens[toknIndex];
                if (!(CurrentToken.Type == TokenType.tSemicolon)) //e.g.  for(... ;; ...) {}   there is no bool so never ending loop or rely on break;
                    boolExprResult = boolexpression();      //evaluate the bool expression       
                if (!boolExprResult)//we can leave the while loop but we still want the parser to show the test happening so fire the event first
                {
                    ParserResult = ParserResult.False;
                    reportProgress(true); //fireEOLEvent(); //we dont go back to process the CR we want to go forward end of the while, so fire event here, 
                    toknIndex = exitStatToknPosn; //finished
                    CurrentToken = Tokens[toknIndex];
                    ParserResult = ParserResult.Ok;
                    return;
                }
                reportProgress(true);
                //carry out whats inside the loop
                toknIndex = trueStatToknPosn;
                CurrentToken = Tokens[toknIndex];
                parseStatement(out breaktype);    //this parse what is inside the FOR loop - use it as a way to set exit token
                if (toknIndex > exitStatToknPosn) //0.7.4 added 
                    exitStatToknPosn = toknIndex;
                //action the loop control
                toknIndex = LoopControlVarPosn;
                CurrentToken = Tokens[toknIndex];
                if (!(CurrentToken.Type == TokenType.tCloseBracket)) //e.g.  for(... ; ... ;) {}   the loop control must be within the loop
                    if (isAssignment(Tokens[toknIndex + 1]))//0.7.1
                        ParseAssignment(out identval, out identType); 
                    else
                        recursiveDescentParser(out identval, out identType); //0.7.1 replaced with if test 
            }
        }
        void ParseSwitch(out Breaktype breaktype) //catches returns
        {
            if (parsetree != null) parsetree.SafeAddtext("parseSwitch");
            dynamic value;
            VarType vartype;
            breaktype = Breaktype.None;
            dynamic testCase;

            int closingBrace;

            getNextTokn();
            matchThenGetNextTokn(TokenType.tOpenBracket);
            recursiveDescentParser(out value, out vartype);
            testCase = value;
            matchThenGetNextTokn(TokenType.tCloseBracket);
            while (toknIndex < Tokens.Count && Tokens[toknIndex].Type == TokenType.tComment)
                getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tCR) //ignore any tCR between while() and trueStat
                getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tLF) //ignore any tCR between while() and trueStat
                getNextTokn();

            matchCurrentTokn(TokenType.tOpenBrace);
            closingBrace = findMatchingBrace(toknIndex);
            getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tCR) //ignore any tCR between while() and trueStat
                getNextTokn();
            if (Tokens[toknIndex].Type == TokenType.tLF) //ignore any tCR between while() and trueStat
                getNextTokn();
            while (toknIndex < Tokens.Count && (CurrentToken.Type == TokenType.tCase || CurrentToken.Type == TokenType.tDefault))
            {
                if (CurrentToken.Type == TokenType.tCase)
                {
                    getNextTokn();
                    recursiveDescentParser(out value, out vartype);
                    matchThenGetNextTokn(TokenType.tColon);
                    if (testCase == value) // this matches the testcase
                    {
                        ParserResult = ParserResult.True;
                        //carry out all the staements until we get a break or return
                        while (toknIndex <= closingBrace &&
                            (Tokens[toknIndex].Type != TokenType.tReturn && Tokens[toknIndex].Type != TokenType.tBreak && Tokens[toknIndex].Type != TokenType.tCase))
                        {
                            parseStatement(out value, out vartype, out breaktype);
                        }
                        if (breaktype == Breaktype.Return)//dont absorb the Return, the function will do that
                            return;
                        matchCurrentTokn(TokenType.tBreak);
                        reportProgress(true);
                        toknIndex = closingBrace;
                        getNextTokn();
                    }
                    else //not a match in value so go to next case / default statement or the end 
                    {
                        while (toknIndex <= closingBrace && Tokens[toknIndex].Type != TokenType.tCase && Tokens[toknIndex].Type != TokenType.tDefault)
                            toknIndex++;
                        toknIndex--;
                    }
                }
                else if (Tokens[toknIndex].Type == TokenType.tDefault)
                {
                    ParserResult = ParserResult.True;
                    getNextTokn();
                    matchThenGetNextTokn(TokenType.tColon);
                    while (toknIndex < closingBrace && Tokens[toknIndex].Type != TokenType.tReturn && Tokens[toknIndex].Type != TokenType.tBreak)
                    {
                        parseStatement(out value, out vartype, out breaktype);
                    }
                }
                getNextTokn();
            }
        }
        #endregion

        #region recursive descent parser
        int ptrOffset;
        bool recursiveDescentParser()
        {
            dynamic value;
            VarType vartype;
            return recursiveDescentParser(out value, out vartype);
        }
        bool recursiveDescentParser(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-lowestPrecedence");
            vartype = VarType.none;
            LogicalOr(out value, out vartype);
            //Assignment(out value, out vartype);
            if (value == null)
            {
                //  value = "error";
                return false;
            }
            else
                return true;
        }
        bool LogicalOr(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-LogicalOr");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tLogicalOr)
                value = 0;
            else //proceed in RDP
                LogicalAnd(out value, out vartype);
            dynamic value2;
            VarType vartype2 = VarType.none;
            while (CurrentToken.Type == TokenType.tLogicalOr && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                getNextTokn();
                LogicalAnd(out value2, out vartype2);
                value = value != 0 ? true : false; //turn numbers into bool for logical test in C#
                value2 = value2 != 0 ? true : false;
                value = value || value2 ? 1 : 0; //after logical test turn bool into 1 for true 0 for false
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool LogicalAnd(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-LogicalAnd");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tLogicalAnd)
                value = 0;
            else
                BitwiseOrOperation(out value, out vartype);
            dynamic value2;
            VarType vartype2 = VarType.none;
            while (CurrentToken.Type == TokenType.tLogicalAnd && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                getNextTokn();
                BitwiseOrOperation(out value2, out vartype2);
                value = value != 0 ? true : false;
                value2 = value2 != 0 ? true : false;
                value = value && value2 ? 1 : 0;
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool BitwiseOrOperation(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-BitwiseOrOperation");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tBitwiseOr)
                value = 0;
            else
                BitwiseExOrOperation(out value, out vartype);
            dynamic value2;
            VarType vartype2 = VarType.none;
            while (CurrentToken.Type == TokenType.tBitwiseOr && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                getNextTokn();
                BitwiseExOrOperation(out value2, out vartype2);
                value |= value2;
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool BitwiseExOrOperation(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-BitwiseExOrOperation");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tBitwiseExOr)
                value = 0;
            else
                BitwiseAndOperation(out value, out vartype);
            dynamic value2;
            VarType vartype2 = VarType.none;
            while (CurrentToken.Type == TokenType.tBitwiseExOr && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                getNextTokn();
                BitwiseAndOperation(out value2, out vartype2);
                value ^= value2;
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool BitwiseAndOperation(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-BitwiseAndOperation");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tBitwiseAnd)
                value = 0;
            else
                LesserGreaterCompare(out value, out vartype);
            dynamic value2;
            VarType vartype2 = VarType.none;
            while (CurrentToken.Type == TokenType.tBitwiseAnd && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                getNextTokn();
                LesserGreaterCompare(out value2, out vartype2);
                value &= value2;
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool LesserGreaterCompare(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-LesserGreaterCompare");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tLessEqual || CurrentToken.Type == TokenType.tLessThan
                || CurrentToken.Type == TokenType.tGreaterEqual || CurrentToken.Type == TokenType.tGreaterThan)
            {
                value = 0;
            }
            else
                EqualityCompare(out value, out vartype);
            while (!backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count &&
                (CurrentToken.Type == TokenType.tLessEqual || CurrentToken.Type == TokenType.tLessThan
                || CurrentToken.Type == TokenType.tGreaterEqual || CurrentToken.Type == TokenType.tGreaterThan))
            {
                dynamic value2;
                VarType vartype2 = VarType.none;
                switch (CurrentToken.Type)
                {
                    case TokenType.tLessEqual:
                        getNextTokn();
                        EqualityCompare(out value2, out vartype2);
                        value = value <= value2 ? 1 : 0;
                        break;
                    case TokenType.tLessThan:
                        getNextTokn();
                        EqualityCompare(out value2, out vartype2);
                        value = value < value2 ? 1 : 0;
                        break;
                    case TokenType.tGreaterEqual:
                        getNextTokn();
                        EqualityCompare(out value2, out vartype2);
                        value = value >= value2 ? 1 : 0;
                        break;
                    case TokenType.tGreaterThan:
                        getNextTokn();
                        EqualityCompare(out value2, out vartype2);
                        value = value > value2 ? 1 : 0;
                        break;
                }
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool EqualityCompare(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-EqualityCompare");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tEqual || CurrentToken.Type == TokenType.tNotEqual)
                value = 0;
            else
                BitwiseShiftOperation(out value, out vartype);
            while ((CurrentToken.Type == TokenType.tEqual || CurrentToken.Type == TokenType.tNotEqual) && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                dynamic value2;
                VarType vartype2 = VarType.none;
                switch (CurrentToken.Type)
                {
                    case TokenType.tEqual:
                        getNextTokn();
                        BitwiseShiftOperation(out value2, out vartype2);
                        value = value == value2 ? 1 : 0;
                        break;
                    case TokenType.tNotEqual:
                        getNextTokn();
                        BitwiseShiftOperation(out value2, out vartype2);
                        value = value != value2 ? 1 : 0;
                        break;
                }
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool BitwiseShiftOperation(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-BitwiseShiftOperation");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tShiftLeft || CurrentToken.Type == TokenType.tShiftRight)
                value = 0;
            else
            {
                Expression(out value, out vartype);
            }
            while ((CurrentToken.Type == TokenType.tShiftLeft || CurrentToken.Type == TokenType.tShiftRight) && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                dynamic value2;
                VarType vartype2 = VarType.none;
                switch (CurrentToken.Type)
                {
                    case TokenType.tShiftLeft:
                        getNextTokn();
                        Expression(out value2, out vartype2);
                        value <<= value2; //2nd operator must be an int
                        //Console.WriteLine("val: " + value);
                        break;
                    case TokenType.tShiftRight:
                        getNextTokn();
                        Expression(out value2, out vartype2);
                        value >>= value2;
                        //Console.WriteLine("val: " + value);
                        break;
                }
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool Expression(out dynamic value, out VarType vartype)           // expression= ['+'|'-'] term {['+'|'-'] term }
        {
            if (parsetree != null) parsetree.SafeAddtext("-Expression");
            vartype = VarType.none;
            value = null;
            if (CurrentToken.Type == TokenType.tAdd || CurrentToken.Type == TokenType.tSubtract) //starts term with +  or - then it is a sign
                value = 0;
            else
            {
                Term(out value, out vartype);
            }
            while ((CurrentToken.Type == TokenType.tAdd || CurrentToken.Type == TokenType.tSubtract)
                && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count) //goes on to include +  or - 
            {
                dynamic value2;
                VarType vartype2 = VarType.none;
                switch (CurrentToken.Type)
                {
                    case TokenType.tAdd:
                        getNextTokn();
                        Term(out value2, out vartype2);
                        value += value2 * ptrOffset;
                        ptrOffset = 1;
                        break;
                    case TokenType.tSubtract:
                        getNextTokn();
                        Term(out value2, out vartype2);
                        value -= value2 * ptrOffset;
                        ptrOffset = 1;
                        //find the best type for the output of the subtraction
                        FindIntType(value, out value, out vartype);
                        //Console.WriteLine("val: " + value);
                        break;
                }
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool Term(out dynamic value, out VarType vartype)                 // term = factor {MulOp factor} - note if a term includes factors it does these first (this gives the precedence)
        {
            if (parsetree != null) parsetree.SafeAddtext("-Term");
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tMultiply || CurrentToken.Type == TokenType.tDivide || CurrentToken.Type == TokenType.tModulus)
            {
                value = null;
            }
            else
            {
                PostFix(out value, out vartype);
                if (value == null)
                {
                    //error(currTokn, " error factor1 is null");
                    return false;
                }
                //Console.WriteLine( "1st factor: typeof =" + value.GetType().ToString() + " value = " + value.ToString());
            }
            while (!backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count &&
                (CurrentToken.Type == TokenType.tMultiply || CurrentToken.Type == TokenType.tDivide || CurrentToken.Type == TokenType.tModulus))
            {
                //here we do the multiplication, so we need to make sure that the type returned is compatible to both types multiplied 
                //- we ignore where it will be assigned to (thats check in assignment)
                //how do we check both types are compatible,
                //first both must be numeric, bool, string should return errors
                dynamic value2;
                VarType vartype2 = VarType.none;
                switch (CurrentToken.Type) //integer types
                {
                    case TokenType.tMultiply:
                        getNextTokn();
                        PostFix(out value2, out vartype2);
                        value *= value2;
                        break;
                    case TokenType.tDivide:
                        getNextTokn();
                        PostFix(out value2, out vartype2);
                        value /= value2;
                        break;
                    case TokenType.tModulus:
                        getNextTokn();
                        PostFix(out value2, out vartype2);
                        value %= value2;
                        break;
                }
                forceCast(vartype, vartype2, out vartype, value, out value);
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool PostFix(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-PostFix");
            value = null;
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tPostIncrement || CurrentToken.Type == TokenType.tPostDecrement)
                value = 0;
            else
            {
                PreFix(out value, out vartype);
            }
            while ((CurrentToken.Type == TokenType.tPostIncrement || CurrentToken.Type == TokenType.tPostDecrement)
                && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                string identName = Tokens[toknIndex - 1].Value;
                switch (CurrentToken.Type)
                {
                    case TokenType.tPostIncrement:
                        postIncrement(identName, out  value, out  vartype);
                        break;
                    case TokenType.tPostDecrement:
                        postDecrement(identName, out  value, out  vartype);
                        break;
                }
                getNextTokn();
            }
            if (value == null)
                return false;
            else
                return true;
        }
        bool PreFix(out dynamic value, out VarType vartype)
        {
            if (parsetree != null) parsetree.SafeAddtext("-PreFix");
            value = null;
            vartype = VarType.none;
            if (CurrentToken.Type == TokenType.tPreIncrement || CurrentToken.Type == TokenType.tPreDecrement) //starts term with +  or - then it is a sign
                value = 0;
            else
            {
                Factor(out value, out vartype);
            }
            while ((CurrentToken.Type == TokenType.tPreIncrement || CurrentToken.Type == TokenType.tPreDecrement)
                && !backgroundWorkerMe.CancellationPending && toknIndex < Tokens.Count)
            {
                string identName;
                switch (CurrentToken.Type) //integer types
                {
                    case TokenType.tPreIncrement: //increment the value in the variable or register then return the incremented value
                        getNextTokn(); //the var
                        identName = CurrentToken.Value;
                        preIncrement(identName, out value, out vartype);
                        break;
                    case TokenType.tPreDecrement:
                        getNextTokn(); //the var
                        identName = CurrentToken.Value;
                        preDecrement(identName, out value, out vartype);
                        break;
                }
                getNextTokn();
            }
            if (value == null)
                return false;
            else
                return true;
        }
        /// <summary>
        /// Factor(out string type, out object value)
        /// Factor is the last in the recursion so the highest in precedence
        /// the role of factor() is to return a long/float/bool/string which can be passed up the recursion for use elsewhere
        /// all the diffeent int type variables are passed around as long (64bit on the PC) not as their individual types,
        /// however that type info goes with them in the enum VarType 'vartype'
        /// Factor must move the currtokn forward to the next tokn
        /// </summary>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        bool Factor(out dynamic value, out VarType vartype)               // factor = '(' expression ')' | boolexpression | varIdent |  number | funcIdent | ~ factor
        {
            value = null;
            vartype = VarType.none;
            string varident;
            if (CurrentToken.Type == TokenType.tVarIdent) //could be in memory, a constant, a symbol(macro/alias), an arr[n] ,  but not a function call
            {
                varident = CurrentToken.Value;
                if (Tokens[toknIndex + 1].Type == TokenType.tOpenSquareBracket)
                {
                    if (parsetree != null) parsetree.SafeAddtext("-Factor - arr ");
                    varident = getArrIdent(CurrentToken);
                }
                if (Memory.VariableExists(varident, ScopeLevel, ScopeName))//(ScopeLevel == 0 ? varident : varident + "_" + ScopeLevel)) //see if the ident is in memory
                {
                    if (parsetree != null) parsetree.SafeAddtext("-Factor - varident ");
                    Variable v = Memory.getVariable(varident, ScopeLevel, ScopeName);//(ScopeLevel == 0 ? varident : varident + "_" + ScopeLevel);
                    value = v.Value;
                    vartype = v.Type;
                }
                else if (Constantstable.ConstantExists(varident)) //0.7.2 so we can use contant values that appear as varIdent
                {
                    Constant c = Constantstable.GetConstant(CurrentToken.Value);
                    value = c.Value;
                    vartype = c.Type;
                }
                else
                {
                    error(CurrentToken, "cannot find a 'variable', 'const' or '#define' of name:  " + CurrentToken.Value);
                }
            }
            else if (CurrentToken.Type == TokenType.tConstIdent) //or is in the constant table
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - constident ");
                Constant c = Constantstable.GetConstant(CurrentToken.Value);
                value = c.Value;
                vartype = c.Type;
            }
            else if (CurrentToken.Type == TokenType.tCharConst)
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - charconst ");
                //turn the string in the value into a char
                char c = CurrentToken.Value[0];
                int i = (int)c;
                value = i;
                vartype = VarType.int8_t;
            }
            else if (CurrentToken.Type == TokenType.tRegAddress)
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - regaddress ");
                byte regValue = 0;
                Registers.ReadRegister(CurrentToken.Value, out regValue); //that class handles any error in reading a register thats not here
                value = regValue;
                vartype = VarType.uint8_t;
            }
            else if (CurrentToken.Type == TokenType.tRegBit)
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - regbit ");
                int bitnumbr = 0;
                bool result = false;
                result = Micro.BitDescriptions.TryGetValue(CurrentToken.Value, out bitnumbr);
                value = bitnumbr;
                if (bitnumbr > 7)
                    vartype = VarType.uint16_t;
                else
                    vartype = VarType.uint8_t;
            }
            else if (CurrentToken.Type == TokenType.tIntConst || CurrentToken.Type == TokenType.tFloatConst || CurrentToken.Type == TokenType.tStringConst) //e.g. 5 or 3.14 or "hello"
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - const ");
                GetAValue(CurrentToken, out value, out vartype);
            }
            else if (CurrentToken.Type == TokenType.tLibCall) //replicated here as well as in statement so that libcalls that return values can be used
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - libcall ");
                parseLibCall(out value, out vartype); //put a break here to see if it every gets called
            }
            else if (CurrentToken.Type == TokenType.tFuncCall) //a funccall should be in the functionsTable, if not pass it to LibraryCall to see if it exists
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - func call ");
                if (Functions.FunctionDefinitionExists(CurrentToken.Value))
                    parseFuncCall(CurrentToken.Value, out value, out vartype);
                else
                    parseLibCall(out value, out vartype);
                if (value == null) value = 0;
            }
            else if (CurrentToken.Type == TokenType.tLogicalNot)// 
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - logical not ");
                getNextTokn();
                dynamic val;

                Factor(out val, out vartype);
                if (val == 0)
                    value = 1;
                else
                    value = 0;
                forceCast(vartype, vartype, out vartype, value, out value); //force the output type to be the same as that going in
                toknIndex -= 2; //alternative to this is to take out the steps back and put getNextToken into onlythe parts we want to 
                getNextTokn();
            }
            else if (CurrentToken.Type == TokenType.tBitwiseNot)// here there is a calculation so we need to check the types are ok as 
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - bitwise not ");
                getNextTokn();
                dynamic val;

                Factor(out val, out vartype);
                value = ~val;
                forceCast(vartype, vartype, out vartype, value, out value); //force the output type to be the same as that going in
                toknIndex -= 2; //alternative to this is to take out the steps back and put getNextToken into onlythe parts we want to 
                getNextTokn();
            }
            else if (CurrentToken.Type == TokenType.tOpenBracket)
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - ( "); getNextTokn();
                if (CurrentToken.Type == TokenType.tType) //cast happening
                {
                    VarType newvartype = getVarType(CurrentToken.Value);
                    getNextTokn();
                    matchCurrentTokn(TokenType.tCloseBracket);
                    getNextTokn();
                    //Factor(out value, out vartype); //removed 0.7.2 as it wasnt working out a symbol that needed to be being calculated
                    recursiveDescentParser(out value, out vartype); //replaced above
                    forceCast(newvartype, newvartype, out vartype, value, out value);
                    toknIndex -= 2;
                    getNextTokn();
                }
                else
                {
                    recursiveDescentParser(out value, out vartype); //parentheses enclosing statement
                    matchCurrentTokn(TokenType.tCloseBracket);
                }
            }
            // *varIdent -we need to find the address, and return the value inside the address e.g. if ptr has 68 in it, then go to 68 and get whats in it
            else if (CurrentToken.Type == TokenType.tDereferenceOp)
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - deref * ");
                getNextTokn();
                if (CurrentToken.Type == TokenType.tVarIdent) //e.g. *ptr 
                {
                    string ptrident = CurrentToken.Value;
                    bool exists = Memory.readValueViaPtr(ptrident, out value, out vartype, ScopeLevel, ScopeName);
                    if (!exists)
                        error(CurrentToken, "no pointer exists");

                }
                else if (CurrentToken.Type == TokenType.tDereferenceOp) //e.g. **ptr or *(
                {
                    
                    Factor(out value, out vartype); //dereference that ptr
                    toknIndex--; //factor eats an extra tokn so go back one
                    CurrentToken = Tokens[toknIndex];
                }
                else if (CurrentToken.Type == TokenType.tOpenBracket)
                {
                    PreFix(out value, out vartype); //dereference that ptr
                    toknIndex--; //factor eats an extra tokn so go back one
                    CurrentToken = Tokens[toknIndex];
                }
                //error(CurrentToken, "expected a variable name");

            }
            else if (CurrentToken.Type == TokenType.tReferenceOp) // &varIdent
            {
                if (parsetree != null) parsetree.SafeAddtext("-Factor - ref & ");
                //return the address of the variable
                getNextTokn();
                matchCurrentTokn(TokenType.tVarIdent);
                varident = CurrentToken.Value;
                if (Tokens[toknIndex + 1].Type == TokenType.tOpenSquareBracket) //LL1
                {
                    varident = getArrIdent(CurrentToken);
                }
                ushort addr;
                bool exists = Memory.getAddress(varident, out addr, ScopeLevel, ScopeName);
                if (exists)
                {
                    value = addr;
                    vartype = Memory.getVariable(varident, ScopeLevel, ScopeName).Type;
                }
                else
                {
                    error(CurrentToken, "variable does not exist");
                    value = null;
                }
            }
            else
            {
                error(CurrentToken, " found  '  " + CurrentToken.Value + "' and was expecting  variable or function or number or  '~' or  '(' ");
            }
            getNextTokn();
            if (value == null)
                return false;
            else
                return true;
        }

        /// <summary>
        /// postIncrement only changes the value of the variable, the result is not passed backup the recursion
        /// </summary>
        /// <param name="identName"></param>
        /// <returns></returns>
        bool postIncrement(string identName, out dynamic value, out VarType vartype)
        {
            value = null;
            dynamic postvalue = null;
            vartype = VarType.none;
            byte registervalue;
            registervalue = 0;
            if (Tokens[toknIndex - 1].Type == TokenType.tVarIdent)
            {
                if (!(readValFromMemory(ref identName, out value, out vartype)) )//we must be able to read something from mem
                    return false;                
                //if its a var return the var contents then incr
                //if its ptr then return value pointed to and then incr ptr by correct amount
                if (vartype.ToString().Contains("ptr"))
                {
                    VarType ptrtype = vartype;
                    if (vartype == VarType.ptr2byteaddr)
                        ptrOffset = 2;
                    if (vartype == VarType.ptr4byteaddr)
                        ptrOffset = 4;
                    int newptrvalue = value + 1 * ptrOffset;
                    Memory.readValueViaPtr(identName, out value, out vartype, ScopeLevel, ScopeName ); //read the value pointed to
                    Memory.writeValue(identName,newptrvalue,ScopeLevel,ScopeName);//write new value to ptr
                    ptrOffset = 1;
                }
                else
                {                      
                    postvalue = value + 1;
                    Memory.writeValue(identName, postvalue, ScopeLevel, ScopeName);//will use identName -which may have been modified by readValFromMmeory
                }
                return true;
            }
            else if (Tokens[toknIndex - 1].Type == TokenType.tRegAddress)
            {
                if (Registers.RegisterExists(identName))
                {
                    try
                    {
                        Registers.ReadRegister(identName, out registervalue);
                        value=registervalue;
                        registervalue++;
                        Registers.WriteRegister(identName, registervalue);
                    }
                    catch
                    {
                        error(CurrentToken, "cannot put " + registervalue.ToString() + " into " + identName);
                        return false;
                    }
                }
            }
            else
            {
                error(CurrentToken, " expected a variable identifier or register address");
                return false;
            }
            return true;
        }
        bool postDecrement(string identName, out dynamic value, out VarType vartype)
        {
            value = null;
            dynamic postvalue = null;
            vartype = VarType.none;
            byte registervalue;
            registervalue = 0;
            if (Tokens[toknIndex - 1].Type == TokenType.tVarIdent)
            {
                if (!(readValFromMemory(ref identName, out value, out vartype)))//we must be able to read something from mem
                    return false;
                //if its a var return the var contents then incr
                //if its ptr then return value pointed to and then incr ptr by correct amount
                if (vartype.ToString().Contains("ptr"))
                {
                    VarType ptrtype = vartype;
                    if (vartype == VarType.ptr2byteaddr)
                        ptrOffset = 2;
                    if (vartype == VarType.ptr4byteaddr)
                        ptrOffset = 4;
                    int newptrvalue = value - 1 * ptrOffset;
                    Memory.readValueViaPtr(identName, out value, out vartype, ScopeLevel, ScopeName); //read the value pointed to
                    Memory.writeValue(identName, newptrvalue, ScopeLevel, ScopeName);//write new value to ptr
                    ptrOffset = 1;
                }
                else
                {
                    postvalue = value - 1;
                    Memory.writeValue(identName, postvalue, ScopeLevel, ScopeName);//will use identName -which may have been modified by readValFromMmeory
                }
                return true;
            }
            else if (Tokens[toknIndex - 1].Type == TokenType.tRegAddress)
            {
                if (Registers.RegisterExists(identName))
                {
                    try
                    {
                        Registers.ReadRegister(identName, out registervalue);
                        value = registervalue;
                        registervalue++;
                        Registers.WriteRegister(identName, registervalue);
                    }
                    catch
                    {
                        error(CurrentToken, "cannot put " + registervalue.ToString() + " into " + identName);
                        return false;
                    }
                }
            }
            else
            {
                error(CurrentToken, " expected a variable identifier or register address");
                return false;
            }
            return true;
        }
        /// <summary>
        /// preIncrement changes the value of the variable and passes the result backup the recusion
        /// </summary>
        /// <param name="identName"></param>
        /// <param name="value"></param>
        /// <param name="vartype"></param>
        /// <returns></returns>
        bool preIncrement(string identName, out dynamic value, out VarType vartype)
        {
            value = null;
            vartype = VarType.none;
            byte registervalue;
            registervalue = 0;
            if (CurrentToken.Type == TokenType.tVarIdent)
            {
                if (readValFromMemory(ref identName, out value, out vartype)) //get the value in memory - note identname may be modifed by the routine to add [i]
                {
                    //if (vartype.ToString().Contains("ptr")) //e.g. ptr++;
                    //{
                    //    Int32 val;
                    //    Int32.TryParse(vartype.ToString().Substring(3, 1), out val);
                    //    value += val;
                    //}
                    //else
                    value += 1 * ptrOffset;
                    ptrOffset = 1;
                    Memory.writeValue(identName, value, ScopeLevel, ScopeName);
                }
                else
                    return false;
            }
            else if (CurrentToken.Type == TokenType.tRegAddress)
            {
                if (Registers.RegisterExists(identName))
                {
                    try
                    {
                        Registers.ReadRegister(identName, out registervalue);
                        ++registervalue;
                        Registers.WriteRegister(identName, registervalue);
                        value = registervalue;
                        vartype = VarType.uint8_t;
                    }
                    catch { error(CurrentToken, "cannot put " + value.ToString() + " into " + identName); }
                }
            }
            else
            {
                error(CurrentToken, " expected a variable identifier or register address");
                return false;
            }
            return true;
        }
        bool preDecrement(string identName, out dynamic value, out VarType vartype)
        {
            value = null;
            vartype = VarType.none;
            byte registervalue;
            registervalue = 0;
            if (CurrentToken.Type == TokenType.tVarIdent)
            {
                if (readValFromMemory(ref identName, out value, out vartype)) //get the value in memory - note identname may be modifed by the routine to add [i]
                {
                    //if (vartype.ToString().Contains("ptr")) //e.g. ptr++;
                    //{
                    //    Int32 val;
                    //    Int32.TryParse(vartype.ToString().Substring(3, 1), out val);
                    //    value -= val;
                    //}
                    //else
                    value -= 1 * ptrOffset;
                    ptrOffset = 1;
                    Memory.writeValue(identName, value, ScopeLevel, ScopeName);
                }
                else
                    return false;
            }
            else if (CurrentToken.Type == TokenType.tRegAddress)
            {
                if (Registers.RegisterExists(identName))
                {
                    try
                    {
                        Registers.ReadRegister(identName, out registervalue);
                        --registervalue;
                        Registers.WriteRegister(identName, registervalue);
                        value = registervalue;
                        vartype = VarType.uint8_t;
                    }
                    catch { error(CurrentToken, "cannot put " + value.ToString() + " into " + identName); }
                }
            }
            else
            {
                error(CurrentToken, " expected a variable identifier or register address");
                return false;
            }
            return true;
        }
        #endregion

        #region pointers
        /// <summary>
        /// enters with varname 
        /// dereferences this name so ptr - looks up value stored in ptr
        /// i.e. this is an address, so looks up that address
        /// </summary>
        /// <returns>true if it can resolve the value in the var pointed to</returns>
        bool dereferenceAPtr(string ptrname, out string varname, out dynamic varvalue, out VarType vartype)
        {
            VarType ptrtype = VarType.none; //the type of the pointer
            Variable v; //the variable we are wanting
            int address = 0;//the address in RAM of the var
            vartype = VarType.none; //the type of the var we are wanting
            varvalue = null; //the value in the var we are wanting
            varname = ""; //the name of the var we are trying to get to
            bool varexists = false;

            v = Memory.getVariable(ptrname, ScopeLevel, ScopeName);
            ptrtype = v.Type;
            address = v.Value;
            varexists = Memory.getVarName(address, out varname); //see if there is a var at that address
            if (varexists)
            {
                v = Memory.getVariable(varname, ScopeLevel, ScopeName); //get the type stored in the variable
                varvalue = v.Value;
                vartype = v.Type;
                //getNextTokn();
                return true;
            }
            else//there is no var at that address
            {
                error(CurrentToken, "could not derefence address " + address.ToString());
                return false;
            }
        }
        bool putValueIntoPtr(int address, dynamic value)
        {
            string varname;
            bool varexists = Memory.getVarName(address, out varname); //see if there is a var at that address
            return true;
        }
        #endregion

        #region helper functions
        int findMatchingBrace(int openBracePosn)
        {
            int closingBracePosn = openBracePosn;
            int braceCounter = 0;
            while (closingBracePosn < Tokens.Count)//make sure we can handle cutoff
            {
                if (Tokens[closingBracePosn].Type == TokenType.tOpenBrace)
                    braceCounter++;
                if (Tokens[closingBracePosn].Type == TokenType.tCloseBrace)
                    braceCounter--;
                if (braceCounter == 0)//found matching closing brace
                {
                    //closingBracePosn++;
                    break;
                }
                closingBracePosn++;
            }
            if (closingBracePosn >= Tokens.Count)//the end of the tokns 
                error(Tokens[openBracePosn], " cannot find a matching closing brace '}' ");
            return closingBracePosn;
        }
        int findMatchingBracket(int openBracketPosn)
        {
            int closingBracketPosn = openBracketPosn;
            int BracketCounter = 0;
            while (closingBracketPosn < Tokens.Count)//make sure we can handle cutoff
            {
                if (Tokens[closingBracketPosn].Type == TokenType.tOpenBracket)
                    BracketCounter++;
                if (Tokens[closingBracketPosn].Type == TokenType.tCloseBracket)
                    BracketCounter--;
                if (BracketCounter == 0)//found matching closing Bracket
                {
                    break;
                }
                closingBracketPosn++;
            }
            if (closingBracketPosn >= Tokens.Count)//the end of the tokns 
                error(Tokens[openBracketPosn], " cannot find a matching closing bracket ')' ");
            return closingBracketPosn;
        }
        bool checkToknIsBool(int boolPosn) //check that the next token is an opening bracket '('
        {
            if (Tokens[boolPosn].Type == TokenType.tOpenBracket)
                return true;
            error(Tokens[boolPosn], " found this instead of '('");
            return false;
        }
        bool isRelOperator(Token t)
        {
            if (t.Type == TokenType.tEqual |
                t.Type == TokenType.tNotEqual |
                t.Type == TokenType.tLessThan |
                t.Type == TokenType.tGreaterThan |
                t.Type == TokenType.tLessEqual |
                t.Type == TokenType.tGreaterEqual)
                return true;
            else
                return false;
        }
        int findNextTrueStatStartPosnAfterBool(int start) //will be the statement after the closing bracket ')' and any tCR 
        {
            int count = start;
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tCloseBracket)
            {
                if (count >= Tokens.Count)
                    error(Tokens[count], " cannot find the bracket ')' at the end of a boolean expression ");
                count++;
            }
            count++; // statement will start here
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR)
                count++;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF)
                count++;
            return count;
        }
        int findNextStatPosnAfterMatchingStatEnd(int start) //at the stat start posn which will be a { or a statement and willreturn the posn of the tokn after the end of the statement
        {
            int count = start;
            if (count < Tokens.Count && Tokens[count].Type == TokenType.tOpenBrace)
            {
                count = findMatchingBrace(count);
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR) //ignore a tCR - should this be a while to absorb multipl tCR??
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF) //ignore a tCR - should this be a while to absorb multipl tCR??
                    count++;
            }
            else//no enclosing braces - so could be on same line or next line, will be a single statement and should have a semicolon
            {
                while (count < Tokens.Count && Tokens[count].Type != TokenType.tSemicolon && Tokens[count].Type != TokenType.tCR && Tokens[count].Type != TokenType.tLF) //tCR added
                {
                    count++;
                }
                count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tCR) //ignore a tCR 
                    count++;
                if (count < Tokens.Count && Tokens[count].Type == TokenType.tLF) //ignore a tCR 
                    count++;
                if (count >= Tokens.Count)
                    error(CurrentToken, " cannot find a semicolon at tthe end of the statement");
            }
            return count;
        }
        int findNextElseIf(int start)
        {
            int count = start;
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tElseIf)
            {
                if (count >= Tokens.Count)
                    return 0; //no tElseIf found
                count++;
            }
            return count;
        }
        int findNextElse(int start)
        {
            int count = start;
            while (count < Tokens.Count && Tokens[count].Type != TokenType.tElse)
            {
                if (count >= Tokens.Count)
                    return 0; //no tElse found
                count++;
            }
            return count;
        }
        int findNextTokenOfType(int start, TokenType toktype)
        {
            int count = start;
            while (count < Tokens.Count && Tokens[count].Type != toktype)
            {
                if (count >= Tokens.Count)
                    return 0; //no tElse found
                count++;
            }
            return count;
        }
        #endregion
    }
}
